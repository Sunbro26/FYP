from __future__ import annotations

import argparse
import copy
import json
import math
import random
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset, WeightedRandomSampler


@dataclass
class FileRecord:
    index: int
    path: str
    style: str
    step_count: int
    start: int
    end: int


@dataclass
class SplitSummary:
    train_files: List[str]
    validation_files: List[str]
    partial_validation_slices: List[Dict[str, object]]
    train_rows: int
    validation_rows: int


@dataclass
class StyleMetrics:
    style: str
    best_epoch: int
    train_loss: float
    validation_loss: float
    train_accuracy: float
    validation_accuracy: float
    train_positive_score_mean: float
    train_negative_score_mean: float
    validation_positive_score_mean: float
    validation_negative_score_mean: float
    input_size: int


@dataclass
class ValidationSlice:
    file_index: int
    path: str
    start_row_in_file: int
    end_row_in_file: int


class StyleCritic(nn.Module):
    def __init__(
        self,
        input_size: int,
        hidden_sizes: Sequence[int],
        input_mean: np.ndarray,
        input_std: np.ndarray,
    ) -> None:
        super().__init__()
        self.register_buffer("input_mean", torch.as_tensor(input_mean, dtype=torch.float32))
        self.register_buffer("input_std", torch.as_tensor(input_std, dtype=torch.float32))

        layers: List[nn.Module] = []
        in_features = input_size
        for hidden_size in hidden_sizes:
            layers.append(nn.Linear(in_features, hidden_size))
            layers.append(nn.ReLU())
            in_features = hidden_size
        layers.append(nn.Linear(in_features, 1))
        self.network = nn.Sequential(*layers)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        normalized = (x - self.input_mean) / self.input_std
        return self.network(normalized).squeeze(-1)


class WeightedMSELoss(nn.Module):
    def __init__(self, positive_weight: float, negative_weight: float) -> None:
        super().__init__()
        self.positive_weight = positive_weight
        self.negative_weight = negative_weight

    def forward(self, predictions: torch.Tensor, targets: torch.Tensor) -> torch.Tensor:
        weights = torch.where(targets > 0, self.positive_weight, self.negative_weight)
        return torch.mean(weights * torch.square(predictions - targets))


def parse_hidden_sizes(text: str) -> List[int]:
    parts = [item.strip() for item in text.split(",") if item.strip()]
    if not parts:
        raise ValueError("At least one hidden layer size must be provided.")
    return [int(item) for item in parts]


def load_manifest(path: Path) -> Dict[str, object]:
    return json.loads(path.read_text())


def require_keys(container: object, required_keys: Sequence[str], label: str) -> None:
    missing = [key for key in required_keys if key not in container]
    if missing:
        raise ValueError(f"{label} is missing required keys: {missing}")


def build_file_records(valid_files: Sequence[Dict[str, object]]) -> List[FileRecord]:
    records: List[FileRecord] = []
    offset = 0
    for index, item in enumerate(valid_files):
        step_count = int(item["step_count"])
        records.append(
            FileRecord(
                index=index,
                path=str(item["path"]),
                style=str(item["style"]),
                step_count=step_count,
                start=offset,
                end=offset + step_count,
            )
        )
        offset += step_count
    return records


def choose_validation_file_indices(
    file_records: Sequence[FileRecord],
    style_order: Sequence[str],
    val_files_per_style: int,
    min_validation_rows: int,
    seed: int,
) -> List[int]:
    rng = random.Random(seed)
    selected: List[int] = []

    for style_name in style_order:
        style_records = [record for record in file_records if record.style == style_name]
        if len(style_records) <= 1:
            continue

        preferred = [record for record in style_records if record.step_count >= min_validation_rows]
        pool = preferred if len(preferred) >= val_files_per_style else style_records
        pool = list(pool)
        rng.shuffle(pool)

        desired = min(val_files_per_style, len(style_records) - 1)
        chosen = sorted(pool[:desired], key=lambda record: record.index)
        if len(chosen) < desired:
            remaining = [record for record in style_records if record.index not in {item.index for item in chosen}]
            rng.shuffle(remaining)
            chosen.extend(sorted(remaining[: desired - len(chosen)], key=lambda record: record.index))

        selected.extend(record.index for record in chosen)

    return sorted(set(selected))


def choose_partial_validation_slices(
    file_records: Sequence[FileRecord],
    style_order: Sequence[str],
    validation_file_indices: Sequence[int],
    row_validation_fraction: float,
) -> List[ValidationSlice]:
    selected = set(validation_file_indices)
    slices: List[ValidationSlice] = []

    for style_name in style_order:
        style_records = [record for record in file_records if record.style == style_name]
        if not style_records:
            continue
        if any(record.index in selected for record in style_records):
            continue

        fallback_record = max(style_records, key=lambda record: record.step_count)
        if fallback_record.step_count < 2:
            raise ValueError(
                f"Style '{style_name}' only has {fallback_record.step_count} row, "
                "which is not enough to create both train and validation splits."
            )

        validation_rows = max(1, int(math.ceil(fallback_record.step_count * row_validation_fraction)))
        validation_rows = min(validation_rows, fallback_record.step_count - 1)
        slices.append(
            ValidationSlice(
                file_index=fallback_record.index,
                path=fallback_record.path,
                start_row_in_file=fallback_record.step_count - validation_rows,
                end_row_in_file=fallback_record.step_count,
            )
        )

    return slices


def build_raw_state_action_inputs(dataset: Dict[str, np.ndarray]) -> np.ndarray:
    return np.concatenate(
        [
            dataset["raw_observations"].astype(np.float32),
            dataset["continuous_actions"].astype(np.float32),
            dataset["discrete_one_hot"].astype(np.float32),
        ],
        axis=1,
    ).astype(np.float32)


def build_style_targets(style_index: np.ndarray, positive_style_index: int) -> np.ndarray:
    targets = np.full(style_index.shape[0], -1.0, dtype=np.float32)
    targets[style_index == positive_style_index] = 1.0
    return targets


def build_row_mask(
    file_records: Sequence[FileRecord],
    selected_file_indices: Sequence[int],
    partial_validation_slices: Sequence[ValidationSlice],
) -> np.ndarray:
    total_rows = file_records[-1].end if file_records else 0
    mask = np.zeros(total_rows, dtype=bool)
    selected = set(selected_file_indices)
    records_by_index = {record.index: record for record in file_records}
    for record in file_records:
        if record.index in selected:
            mask[record.start : record.end] = True
    for item in partial_validation_slices:
        record = records_by_index.get(item.file_index)
        if record is None:
            raise ValueError(f"Validation slice references unknown file index {item.file_index}.")
        start = record.start + item.start_row_in_file
        end = record.start + item.end_row_in_file
        mask[start:end] = True
    return mask


def validate_dataset_contract(manifest: Dict[str, object], dataset: Dict[str, np.ndarray]) -> None:
    require_keys(
        manifest,
        [
            "style_order",
            "valid_files",
            "total_steps",
            "raw_observation_size",
            "continuous_action_size",
            "effective_discrete_branch_size",
        ],
        "manifest",
    )
    require_keys(
        dataset,
        [
            "raw_observations",
            "continuous_actions",
            "discrete_one_hot",
            "style_index",
        ],
        "dataset",
    )

    total_rows = int(dataset["style_index"].shape[0])
    expected_rows = int(manifest["total_steps"])
    if total_rows != expected_rows:
        raise ValueError(
            f"Dataset row count {total_rows} does not match manifest total_steps {expected_rows}."
        )

    row_aligned_keys = [
        "raw_observations",
        "continuous_actions",
        "discrete_one_hot",
        "style_index",
        "file_index",
        "row_in_file",
        "critic_inputs",
    ]
    for key in row_aligned_keys:
        if key not in dataset:
            continue
        if int(dataset[key].shape[0]) != total_rows:
            raise ValueError(
                f"Dataset field '{key}' has {dataset[key].shape[0]} rows, expected {total_rows}."
            )

    raw_width = int(dataset["raw_observations"].shape[1])
    continuous_width = int(dataset["continuous_actions"].shape[1])
    discrete_width = int(dataset["discrete_one_hot"].shape[1])

    if raw_width != int(manifest["raw_observation_size"]):
        raise ValueError(
            f"Raw observation width {raw_width} does not match manifest value {manifest['raw_observation_size']}."
        )
    if continuous_width != int(manifest["continuous_action_size"]):
        raise ValueError(
            f"Continuous action width {continuous_width} does not match manifest value {manifest['continuous_action_size']}."
        )
    if discrete_width != int(manifest["effective_discrete_branch_size"]):
        raise ValueError(
            f"Discrete one-hot width {discrete_width} does not match manifest value {manifest['effective_discrete_branch_size']}."
        )

    style_order = [str(name) for name in manifest["style_order"]]
    if total_rows > 0:
        observed_style_min = int(np.min(dataset["style_index"]))
        observed_style_max = int(np.max(dataset["style_index"]))
        if observed_style_min < 0 or observed_style_max >= len(style_order):
            raise ValueError(
                f"Style indices must stay within [0, {len(style_order) - 1}], "
                f"found range [{observed_style_min}, {observed_style_max}]."
            )

    if "critic_inputs" in dataset:
        rebuilt_critic_inputs = build_raw_state_action_inputs(dataset)
        exported_critic_inputs = dataset["critic_inputs"].astype(np.float32)
        if rebuilt_critic_inputs.shape != exported_critic_inputs.shape:
            raise ValueError(
                "Exported critic_inputs shape does not match the reconstructed "
                "raw_observations + continuous_actions + discrete_one_hot shape."
            )
        if not np.allclose(rebuilt_critic_inputs, exported_critic_inputs, atol=1e-5):
            raise ValueError(
                "Exported critic_inputs does not match the reconstructed "
                "raw_observations + continuous_actions + discrete_one_hot contract."
            )


@torch.no_grad()
def evaluate_model(
    model: nn.Module,
    inputs: np.ndarray,
    targets: np.ndarray,
    device: torch.device,
    batch_size: int,
    positive_weight: float,
    negative_weight: float,
) -> Dict[str, float]:
    model.eval()
    criterion = WeightedMSELoss(positive_weight=positive_weight, negative_weight=negative_weight)

    dataset = TensorDataset(
        torch.from_numpy(inputs.astype(np.float32)),
        torch.from_numpy(targets.astype(np.float32)),
    )
    loader = DataLoader(dataset, batch_size=batch_size, shuffle=False)

    total_loss = 0.0
    total_rows = 0
    predictions: List[np.ndarray] = []
    labels: List[np.ndarray] = []

    for batch_inputs, batch_targets in loader:
        batch_inputs = batch_inputs.to(device)
        batch_targets = batch_targets.to(device)
        batch_predictions = model(batch_inputs)
        batch_loss = criterion(batch_predictions, batch_targets)
        rows = int(batch_targets.shape[0])
        total_loss += float(batch_loss.item()) * rows
        total_rows += rows
        predictions.append(batch_predictions.cpu().numpy())
        labels.append(batch_targets.cpu().numpy())

    all_predictions = np.concatenate(predictions, axis=0) if predictions else np.zeros((0,), dtype=np.float32)
    all_labels = np.concatenate(labels, axis=0) if labels else np.zeros((0,), dtype=np.float32)
    predicted_positive = all_predictions >= 0.0
    actual_positive = all_labels > 0.0
    accuracy = float(np.mean(predicted_positive == actual_positive)) if total_rows > 0 else 0.0

    positive_scores = all_predictions[actual_positive]
    negative_scores = all_predictions[~actual_positive]

    return {
        "loss": total_loss / max(1, total_rows),
        "accuracy": accuracy,
        "positive_score_mean": float(np.mean(positive_scores)) if positive_scores.size else 0.0,
        "negative_score_mean": float(np.mean(negative_scores)) if negative_scores.size else 0.0,
    }


def build_balanced_sampler(targets: np.ndarray, seed: int) -> WeightedRandomSampler:
    positive_mask = targets > 0.0
    negative_mask = ~positive_mask
    positive_count = int(np.sum(positive_mask))
    negative_count = int(np.sum(negative_mask))
    if positive_count == 0 or negative_count == 0:
        raise ValueError("Training split must contain both positive and negative samples.")

    weights = np.where(
        positive_mask,
        1.0 / positive_count,
        1.0 / negative_count,
    ).astype(np.float64)

    generator = torch.Generator()
    generator.manual_seed(seed)
    return WeightedRandomSampler(
        weights=torch.from_numpy(weights),
        num_samples=targets.shape[0],
        replacement=True,
        generator=generator,
    )


def train_one_style(
    style_name: str,
    positive_style_index: int,
    inputs: np.ndarray,
    style_index: np.ndarray,
    file_records: Sequence[FileRecord],
    validation_file_indices: Sequence[int],
    partial_validation_slices: Sequence[ValidationSlice],
    output_dir: Path,
    hidden_sizes: Sequence[int],
    batch_size: int,
    epochs: int,
    patience: int,
    learning_rate: float,
    weight_decay: float,
    seed: int,
    device: torch.device,
    critic_contract: Dict[str, object],
) -> Tuple[StyleMetrics, SplitSummary, Path, Path, Path]:
    row_is_validation = build_row_mask(
        file_records,
        validation_file_indices,
        partial_validation_slices,
    )
    train_mask = ~row_is_validation
    validation_mask = row_is_validation

    targets = build_style_targets(style_index, positive_style_index)

    train_inputs = inputs[train_mask]
    train_targets = targets[train_mask]
    validation_inputs = inputs[validation_mask]
    validation_targets = targets[validation_mask]

    if train_inputs.shape[0] == 0 or validation_inputs.shape[0] == 0:
        raise ValueError(f"Style '{style_name}' does not have enough train/validation data.")

    train_positive_count = int(np.sum(train_targets > 0))
    train_negative_count = int(np.sum(train_targets < 0))
    if train_positive_count == 0 or train_negative_count == 0:
        raise ValueError(f"Style '{style_name}' training split must contain both classes.")

    positive_weight = 0.5 / train_positive_count
    negative_weight = 0.5 / train_negative_count

    input_mean = train_inputs.mean(axis=0).astype(np.float32)
    input_std = train_inputs.std(axis=0).astype(np.float32)
    input_std[input_std < 1e-6] = 1.0

    model = StyleCritic(
        input_size=train_inputs.shape[1],
        hidden_sizes=hidden_sizes,
        input_mean=input_mean,
        input_std=input_std,
    ).to(device)

    optimizer = torch.optim.Adam(
        model.parameters(),
        lr=learning_rate,
        weight_decay=weight_decay,
    )
    criterion = WeightedMSELoss(
        positive_weight=positive_weight,
        negative_weight=negative_weight,
    )

    train_dataset = TensorDataset(
        torch.from_numpy(train_inputs.astype(np.float32)),
        torch.from_numpy(train_targets.astype(np.float32)),
    )
    sampler = build_balanced_sampler(train_targets, seed + positive_style_index)
    train_loader = DataLoader(
        train_dataset,
        batch_size=batch_size,
        sampler=sampler,
        drop_last=False,
    )

    best_epoch = 0
    best_validation_loss = math.inf
    best_state: Optional[Dict[str, torch.Tensor]] = None
    epochs_without_improvement = 0

    for epoch in range(1, epochs + 1):
        model.train()
        for batch_inputs, batch_targets in train_loader:
            batch_inputs = batch_inputs.to(device)
            batch_targets = batch_targets.to(device)
            optimizer.zero_grad(set_to_none=True)
            predictions = model(batch_inputs)
            loss = criterion(predictions, batch_targets)
            loss.backward()
            optimizer.step()

        validation_metrics = evaluate_model(
            model=model,
            inputs=validation_inputs,
            targets=validation_targets,
            device=device,
            batch_size=batch_size,
            positive_weight=positive_weight,
            negative_weight=negative_weight,
        )

        if validation_metrics["loss"] < best_validation_loss:
            best_validation_loss = validation_metrics["loss"]
            best_epoch = epoch
            best_state = copy.deepcopy(model.state_dict())
            epochs_without_improvement = 0
        else:
            epochs_without_improvement += 1
            if epochs_without_improvement >= patience:
                break

    if best_state is None:
        raise RuntimeError(f"Training for style '{style_name}' did not produce a valid checkpoint.")

    model.load_state_dict(best_state)

    train_metrics = evaluate_model(
        model=model,
        inputs=train_inputs,
        targets=train_targets,
        device=device,
        batch_size=batch_size,
        positive_weight=positive_weight,
        negative_weight=negative_weight,
    )
    validation_metrics = evaluate_model(
        model=model,
        inputs=validation_inputs,
        targets=validation_targets,
        device=device,
        batch_size=batch_size,
        positive_weight=positive_weight,
        negative_weight=negative_weight,
    )

    style_output_dir = output_dir / style_name
    style_output_dir.mkdir(parents=True, exist_ok=True)

    checkpoint_path = style_output_dir / f"{style_name}_critic.pt"
    onnx_path = style_output_dir / f"{style_name}_critic.onnx"
    metadata_path = style_output_dir / f"{style_name}_critic_metadata.json"

    torch.save(
        {
            "style": style_name,
            "positive_style_index": positive_style_index,
            "input_size": int(train_inputs.shape[1]),
            "hidden_sizes": list(hidden_sizes),
            "state_dict": model.state_dict(),
            "input_mean": input_mean,
            "input_std": input_std,
        },
        checkpoint_path,
    )

    model.eval()
    dummy_input = torch.zeros(1, train_inputs.shape[1], dtype=torch.float32, device=device)
    torch.onnx.export(
        model,
        dummy_input,
        str(onnx_path),
        input_names=["state_action"],
        output_names=["discriminator_score"],
        dynamic_axes={
            "state_action": {0: "batch"},
            "discriminator_score": {0: "batch"},
        },
        opset_version=13,
    )

    train_file_paths = [record.path for record in file_records if record.index not in set(validation_file_indices)]
    validation_file_paths = [record.path for record in file_records if record.index in set(validation_file_indices)]
    for item in partial_validation_slices:
        if item.path not in validation_file_paths:
            validation_file_paths.append(item.path)
    split_summary = SplitSummary(
        train_files=train_file_paths,
        validation_files=validation_file_paths,
        partial_validation_slices=[asdict(item) for item in partial_validation_slices],
        train_rows=int(train_inputs.shape[0]),
        validation_rows=int(validation_inputs.shape[0]),
    )

    metrics = StyleMetrics(
        style=style_name,
        best_epoch=best_epoch,
        train_loss=float(train_metrics["loss"]),
        validation_loss=float(validation_metrics["loss"]),
        train_accuracy=float(train_metrics["accuracy"]),
        validation_accuracy=float(validation_metrics["accuracy"]),
        train_positive_score_mean=float(train_metrics["positive_score_mean"]),
        train_negative_score_mean=float(train_metrics["negative_score_mean"]),
        validation_positive_score_mean=float(validation_metrics["positive_score_mean"]),
        validation_negative_score_mean=float(validation_metrics["negative_score_mean"]),
        input_size=int(train_inputs.shape[1]),
    )

    metadata = {
        "style": style_name,
        "positive_style_index": positive_style_index,
        "input_size": int(train_inputs.shape[1]),
        "hidden_sizes": list(hidden_sizes),
        "best_epoch": best_epoch,
        "loss_type": "weighted_lsgan_mse",
        "target_scores": {"positive": 1.0, "negative": -1.0},
        "critic_contract": critic_contract,
        "train_metrics": asdict(metrics),
        "split": asdict(split_summary),
    }
    metadata_path.write_text(json.dumps(metadata, indent=2))

    return metrics, split_summary, checkpoint_path, onnx_path, metadata_path


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Train one MultiGAIL style critic per style from exported skeleton demo datasets."
    )
    parser.add_argument(
        "--dataset-dir",
        type=Path,
        default=Path("results/multigail_dataset"),
        help="Directory containing skeleton_multigail_dataset.npz and manifest.json.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("results/multigail_critics"),
        help="Directory where critic checkpoints, ONNX files, and metadata will be written.",
    )
    parser.add_argument(
        "--hidden-sizes",
        type=str,
        default="256,256",
        help="Comma-separated MLP hidden sizes. Default: 256,256",
    )
    parser.add_argument(
        "--batch-size",
        type=int,
        default=256,
        help="Mini-batch size. Default: 256",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=50,
        help="Maximum number of training epochs per style. Default: 50",
    )
    parser.add_argument(
        "--patience",
        type=int,
        default=8,
        help="Early stopping patience on validation loss. Default: 8",
    )
    parser.add_argument(
        "--learning-rate",
        type=float,
        default=3e-4,
        help="Adam learning rate. Default: 3e-4",
    )
    parser.add_argument(
        "--weight-decay",
        type=float,
        default=1e-5,
        help="Adam weight decay. Default: 1e-5",
    )
    parser.add_argument(
        "--val-files-per-style",
        type=int,
        default=1,
        help="How many demo files per style to reserve for validation. Default: 1",
    )
    parser.add_argument(
        "--min-validation-rows",
        type=int,
        default=128,
        help="Prefer validation files with at least this many rows. Default: 128",
    )
    parser.add_argument(
        "--row-validation-fraction",
        type=float,
        default=0.2,
        help="Fallback validation fraction for styles that do not have a separate validation file. Default: 0.2",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=7,
        help="Random seed used for validation file selection and sampler state. Default: 7",
    )
    parser.add_argument(
        "--device",
        type=str,
        default="auto",
        choices=["auto", "cpu", "cuda"],
        help="Training device. Default: auto",
    )
    return parser


def resolve_device(device_arg: str) -> torch.device:
    if device_arg == "cpu":
        return torch.device("cpu")
    if device_arg == "cuda":
        if not torch.cuda.is_available():
            raise ValueError("CUDA was requested but is not available.")
        return torch.device("cuda")
    return torch.device("cuda" if torch.cuda.is_available() else "cpu")


def main() -> int:
    parser = build_arg_parser()
    args = parser.parse_args()

    if args.row_validation_fraction <= 0.0 or args.row_validation_fraction >= 1.0:
        raise ValueError("--row-validation-fraction must be greater than 0 and less than 1.")

    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)

    hidden_sizes = parse_hidden_sizes(args.hidden_sizes)
    device = resolve_device(args.device)

    manifest_path = args.dataset_dir / "manifest.json"
    dataset_path = args.dataset_dir / "skeleton_multigail_dataset.npz"
    manifest = load_manifest(manifest_path)
    dataset = np.load(dataset_path)
    validate_dataset_contract(manifest, dataset)

    file_records = build_file_records(manifest["valid_files"])
    style_order = [str(name) for name in manifest["style_order"]]
    validation_file_indices = choose_validation_file_indices(
        file_records=file_records,
        style_order=style_order,
        val_files_per_style=args.val_files_per_style,
        min_validation_rows=args.min_validation_rows,
        seed=args.seed,
    )
    partial_validation_slices = choose_partial_validation_slices(
        file_records=file_records,
        style_order=style_order,
        validation_file_indices=validation_file_indices,
        row_validation_fraction=args.row_validation_fraction,
    )

    raw_state_action_inputs = build_raw_state_action_inputs(dataset)
    style_index = dataset["style_index"].astype(np.int64)
    critic_contract = {
        "input_name": "state_action",
        "output_name": "discriminator_score",
        "raw_observation_size": int(manifest["raw_observation_size"]),
        "continuous_action_size": int(manifest["continuous_action_size"]),
        "discrete_one_hot_size": int(manifest["effective_discrete_branch_size"]),
        "critic_input_size": int(raw_state_action_inputs.shape[1]),
        "style_conditioning_used_by_policy": bool(manifest.get("append_style_conditioning", False)),
        "style_conditioning_used_by_critic": False,
        "loss_type": "weighted_lsgan_mse",
        "target_scores": {"positive": 1.0, "negative": -1.0},
        "reward_transform": "max(0, 1 - 0.25 * (score - 1)^2)",
    }

    args.output_dir.mkdir(parents=True, exist_ok=True)

    all_metrics: List[StyleMetrics] = []
    per_style_outputs: Dict[str, Dict[str, object]] = {}

    for positive_style_index, style_name in enumerate(style_order):
        metrics, split_summary, checkpoint_path, onnx_path, metadata_path = train_one_style(
            style_name=style_name,
            positive_style_index=positive_style_index,
            inputs=raw_state_action_inputs,
            style_index=style_index,
            file_records=file_records,
            validation_file_indices=validation_file_indices,
            partial_validation_slices=partial_validation_slices,
            output_dir=args.output_dir,
            hidden_sizes=hidden_sizes,
            batch_size=args.batch_size,
            epochs=args.epochs,
            patience=args.patience,
            learning_rate=args.learning_rate,
            weight_decay=args.weight_decay,
            seed=args.seed,
            device=device,
            critic_contract=critic_contract,
        )
        all_metrics.append(metrics)
        per_style_outputs[style_name] = {
            "checkpoint": str(checkpoint_path),
            "onnx": str(onnx_path),
            "metadata": str(metadata_path),
            "split": asdict(split_summary),
            "metrics": asdict(metrics),
        }

    summary = {
        "dataset_dir": str(args.dataset_dir),
        "output_dir": str(args.output_dir),
        "device": str(device),
        "styles": style_order,
        "critic_contract": critic_contract,
        "validation_file_indices": validation_file_indices,
        "partial_validation_slices": [asdict(item) for item in partial_validation_slices],
        "outputs": per_style_outputs,
    }
    summary_path = args.output_dir / "training_summary.json"
    summary_path.write_text(json.dumps(summary, indent=2))

    print("Trained MultiGAIL critics")
    print(f"  dataset: {args.dataset_dir}")
    print(f"  output: {args.output_dir}")
    print(f"  device: {device}")
    print(f"  critic input size: {raw_state_action_inputs.shape[1]}")
    print(f"  validation files: {validation_file_indices}")
    if partial_validation_slices:
        print(f"  partial validation slices: {[asdict(item) for item in partial_validation_slices]}")
    for metrics in all_metrics:
        print(
            f"  [{metrics.style}] epoch={metrics.best_epoch} "
            f"train_loss={metrics.train_loss:.4f} val_loss={metrics.validation_loss:.4f} "
            f"train_acc={metrics.train_accuracy:.3f} val_acc={metrics.validation_accuracy:.3f}"
        )
    print(f"  summary: {summary_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
