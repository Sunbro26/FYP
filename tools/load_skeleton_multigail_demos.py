from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np
from mlagents.trainers.demo_loader import load_demonstration
from mlagents_envs.base_env import BehaviorSpec


@dataclass
class DemoFileSummary:
    path: str
    style: str
    size_bytes: int
    step_count: int
    raw_observation_size: int
    conditioned_observation_size: int
    continuous_action_size: int
    declared_discrete_branch_size: int
    effective_discrete_branch_size: int
    max_raw_discrete_action: int


@dataclass
class SkippedDemo:
    path: str
    reason: str


@dataclass
class DatasetSummary:
    style_order: List[str]
    valid_files: List[DemoFileSummary]
    skipped_files: List[SkippedDemo]
    total_steps: int
    raw_observation_size: Optional[int]
    conditioned_observation_size: Optional[int]
    continuous_action_size: Optional[int]
    effective_discrete_branch_size: Optional[int]
    critic_input_size: Optional[int]
    style_file_counts: Dict[str, int]
    style_step_counts: Dict[str, int]
    available_raw_observation_sizes: List[int]
    schema_report: Dict[int, Dict[str, object]]


@dataclass
class ParsedDemoRows:
    observations: np.ndarray
    continuous_actions: np.ndarray
    discrete_actions: np.ndarray
    raw_observation_size: int
    continuous_action_size: int
    declared_discrete_branch_size: int
    observed_max_discrete_action: int
    step_count: int


@dataclass
class CandidateDemo:
    path: Path
    style: str
    size_bytes: int
    parsed: ParsedDemoRows
    remapped_discrete: np.ndarray
    effective_branch_size: int


def parse_style_args(style_args: Sequence[str]) -> List[Tuple[str, str]]:
    styles: List[Tuple[str, str]] = []
    for item in style_args:
        if "=" not in item:
            raise ValueError(f"Invalid --style '{item}'. Use name=prefix.")
        name, prefix = item.split("=", 1)
        name = name.strip().lower()
        prefix = prefix.strip()
        if not name or not prefix:
            raise ValueError(f"Invalid --style '{item}'. Use name=prefix.")
        styles.append((name, prefix))
    if not styles:
        styles = [
            ("aggressive", "SkeletonAggressi"),
            ("defensive", "SkeletonDefensiv"),
        ]
    return styles


def parse_remap_args(remap_args: Sequence[str]) -> Dict[int, int]:
    mapping: Dict[int, int] = {}
    for item in remap_args:
        if "=" not in item:
            raise ValueError(f"Invalid --remap-discrete '{item}'. Use from=to.")
        old_text, new_text = item.split("=", 1)
        mapping[int(old_text.strip())] = int(new_text.strip())
    return mapping


def infer_style(path: Path, style_prefixes: Sequence[Tuple[str, str]]) -> Optional[str]:
    stem = path.stem
    for style_name, prefix in style_prefixes:
        if stem.startswith(prefix):
            return style_name
    return None


def flatten_vector_observations(behavior_spec: BehaviorSpec, pair) -> np.ndarray:
    pieces: List[np.ndarray] = []
    for obs_spec, obs in zip(behavior_spec.observation_specs, pair.agent_info.observations):
        if len(obs_spec.shape) != 1:
            raise ValueError(
                f"Only 1D vector observations are supported, got shape {obs_spec.shape}."
            )
        values = np.asarray(obs.float_data.data, dtype=np.float32)
        expected = int(np.prod(obs_spec.shape))
        if values.size != expected:
            raise ValueError(
                f"Observation size mismatch. Expected {expected} floats, found {values.size}."
            )
        pieces.append(values)
    if not pieces:
        raise ValueError("No vector observations found in demo row.")
    return np.concatenate(pieces, dtype=np.float32)


def parse_demo_rows(path: Path) -> Tuple[BehaviorSpec, ParsedDemoRows]:
    behavior_spec, pairs, _ = load_demonstration(str(path))

    action_spec = behavior_spec.action_spec
    if action_spec.discrete_size != 1:
        raise ValueError(
            f"Expected exactly one discrete branch, found {action_spec.discrete_branches}."
        )

    raw_observations: List[np.ndarray] = []
    continuous_actions: List[np.ndarray] = []
    discrete_actions: List[int] = []

    declared_branch_size = int(action_spec.discrete_branches[0])
    continuous_size = int(action_spec.continuous_size)

    for pair in pairs:
        raw_obs = flatten_vector_observations(behavior_spec, pair)
        raw_observations.append(raw_obs)

        cont = np.asarray(pair.action_info.continuous_actions, dtype=np.float32)
        if cont.size != continuous_size:
            raise ValueError(
                f"Continuous action size mismatch. Expected {continuous_size}, found {cont.size}."
            )
        continuous_actions.append(cont)

        disc = np.asarray(pair.action_info.discrete_actions, dtype=np.int64)
        if disc.size != 1:
            raise ValueError(f"Expected one discrete action value, found {disc.size}.")
        disc_value = int(disc[0])
        if disc_value < 0:
            raise ValueError(f"Discrete action {disc_value} cannot be negative.")
        discrete_actions.append(disc_value)

    if not raw_observations:
        raise ValueError("Demo contained no action rows.")

    obs_array = np.stack(raw_observations).astype(np.float32)
    cont_array = np.stack(continuous_actions).astype(np.float32)
    disc_array = np.asarray(discrete_actions, dtype=np.int64)

    return behavior_spec, ParsedDemoRows(
        observations=obs_array,
        continuous_actions=cont_array,
        discrete_actions=disc_array,
        raw_observation_size=int(obs_array.shape[1]),
        continuous_action_size=continuous_size,
        declared_discrete_branch_size=declared_branch_size,
        observed_max_discrete_action=int(disc_array.max()),
        step_count=int(obs_array.shape[0]),
    )


def build_style_vector(style_name: str, style_order: Sequence[str]) -> np.ndarray:
    vec = np.zeros(len(style_order), dtype=np.float32)
    vec[style_order.index(style_name)] = 1.0
    return vec


def remap_discrete_actions(discrete_actions: np.ndarray, remap: Dict[int, int]) -> np.ndarray:
    if not remap:
        return discrete_actions.astype(np.int64, copy=True)
    result = discrete_actions.astype(np.int64, copy=True)
    for old_value, new_value in remap.items():
        result[result == old_value] = new_value
    return result


def determine_effective_branch_size(
    remapped_actions: np.ndarray,
    expected_branch_size: Optional[int],
) -> int:
    if remapped_actions.size == 0:
        raise ValueError("No discrete actions were found in the demo rows.")
    observed = int(remapped_actions.max()) + 1
    if expected_branch_size is None:
        return observed
    if observed > expected_branch_size:
        raise ValueError(
            f"Remapped actions require branch size {observed}, which exceeds expected {expected_branch_size}."
        )
    return expected_branch_size


def make_discrete_one_hot(discrete_actions: np.ndarray, branch_size: int) -> np.ndarray:
    if np.any(discrete_actions < 0) or np.any(discrete_actions >= branch_size):
        bad = discrete_actions[(discrete_actions < 0) | (discrete_actions >= branch_size)][0]
        raise ValueError(
            f"Discrete action {int(bad)} is outside effective branch size {branch_size}."
        )
    one_hot = np.zeros((discrete_actions.shape[0], branch_size), dtype=np.float32)
    one_hot[np.arange(discrete_actions.shape[0]), discrete_actions.astype(np.int64)] = 1.0
    return one_hot


def concatenate_critic_inputs(
    conditioned_observations: np.ndarray,
    continuous_actions: np.ndarray,
    discrete_one_hot: np.ndarray,
) -> np.ndarray:
    return np.concatenate(
        [conditioned_observations, continuous_actions, discrete_one_hot],
        axis=1,
    ).astype(np.float32)


def build_schema_report(
    candidates: Sequence[CandidateDemo],
    style_order: Sequence[str],
) -> Dict[int, Dict[str, object]]:
    report: Dict[int, Dict[str, object]] = {}
    for candidate in candidates:
        obs_size = candidate.parsed.raw_observation_size
        entry = report.setdefault(
            obs_size,
            {
                "total_files": 0,
                "total_steps": 0,
                "styles": {
                    style_name: {"files": 0, "steps": 0}
                    for style_name in style_order
                },
            },
        )
        entry["total_files"] += 1
        entry["total_steps"] += candidate.parsed.step_count
        style_stats = entry["styles"][candidate.style]
        style_stats["files"] += 1
        style_stats["steps"] += candidate.parsed.step_count

    for entry in report.values():
        entry["covered_styles"] = [
            style_name
            for style_name in style_order
            if entry["styles"][style_name]["files"] > 0
        ]

    return report


def choose_target_raw_observation_size(
    schema_report: Dict[int, Dict[str, object]],
    style_order: Sequence[str],
    expected_obs_size: Optional[int],
) -> int:
    if expected_obs_size is not None:
        return expected_obs_size
    if not schema_report:
        raise ValueError("No parseable demo files were found.")

    compatible_sizes = [
        obs_size
        for obs_size, report in schema_report.items()
        if all(report["styles"][style_name]["files"] > 0 for style_name in style_order)
    ]
    if not compatible_sizes:
        coverage = ", ".join(
            f"{obs_size}: {report['covered_styles']}"
            for obs_size, report in sorted(schema_report.items())
        )
        raise ValueError(
            "No raw observation size is shared across all requested styles. "
            f"Available coverage: {coverage}"
        )

    compatible_sizes.sort(
        key=lambda obs_size: (
            int(schema_report[obs_size]["total_steps"]),
            int(schema_report[obs_size]["total_files"]),
            obs_size,
        ),
        reverse=True,
    )
    return compatible_sizes[0]


def save_npz(path: Path, payload: Dict[str, np.ndarray]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(path, **payload)


def load_dataset(
    demo_root: Path,
    style_prefixes: Sequence[Tuple[str, str]],
    min_bytes: int,
    append_style_conditioning: bool,
    expected_obs_size: Optional[int],
    expected_continuous_size: Optional[int],
    expected_discrete_branch_size: Optional[int],
    discrete_remap: Dict[int, int],
    output_dir: Optional[Path],
) -> DatasetSummary:
    style_order = [name for name, _ in style_prefixes]
    skipped_files: List[SkippedDemo] = []
    candidates: List[CandidateDemo] = []

    demo_files = sorted(demo_root.rglob("*.demo"))
    for demo_path in demo_files:
        style_name = infer_style(demo_path, style_prefixes)
        if style_name is None:
            skipped_files.append(SkippedDemo(str(demo_path), "No matching style prefix."))
            continue

        size_bytes = demo_path.stat().st_size
        if size_bytes < min_bytes:
            skipped_files.append(
                SkippedDemo(str(demo_path), f"File too small ({size_bytes} bytes).")
            )
            continue

        try:
            _, parsed = parse_demo_rows(demo_path)
            remapped_discrete = remap_discrete_actions(parsed.discrete_actions, discrete_remap)
            effective_branch_size = determine_effective_branch_size(
                remapped_discrete,
                expected_discrete_branch_size,
            )
            if (
                expected_continuous_size is not None
                and parsed.continuous_action_size != expected_continuous_size
            ):
                raise ValueError(
                    "Continuous action size did not match the configured runtime. "
                    f"Expected {expected_continuous_size}, found {parsed.continuous_action_size}."
                )
            candidates.append(
                CandidateDemo(
                    path=demo_path,
                    style=style_name,
                    size_bytes=size_bytes,
                    parsed=parsed,
                    remapped_discrete=remapped_discrete,
                    effective_branch_size=effective_branch_size,
                )
            )
        except Exception as exc:
            skipped_files.append(SkippedDemo(str(demo_path), str(exc)))

    schema_report = build_schema_report(candidates, style_order)
    target_raw_obs_size = choose_target_raw_observation_size(
        schema_report,
        style_order,
        expected_obs_size,
    )

    valid_files: List[DemoFileSummary] = []
    combined_obs: List[np.ndarray] = []
    combined_conditioned_obs: List[np.ndarray] = []
    combined_cont: List[np.ndarray] = []
    combined_disc: List[np.ndarray] = []
    combined_one_hot: List[np.ndarray] = []
    combined_style_index: List[np.ndarray] = []

    per_style_buffers: Dict[str, Dict[str, List[np.ndarray]]] = {
        style_name: {
            "raw_obs": [],
            "conditioned_obs": [],
            "continuous": [],
            "discrete": [],
            "one_hot": [],
            "style_index": [],
        }
        for style_name in style_order
    }

    for candidate in candidates:
        if candidate.parsed.raw_observation_size != target_raw_obs_size:
            skipped_files.append(
                SkippedDemo(
                    str(candidate.path),
                    "Raw observation size did not match the chosen dataset schema. "
                    f"Expected {target_raw_obs_size}, found {candidate.parsed.raw_observation_size}.",
                )
            )
            continue

        discrete_one_hot = make_discrete_one_hot(
            candidate.remapped_discrete,
            candidate.effective_branch_size,
        )
        style_vector = build_style_vector(candidate.style, style_order)
        conditioned_obs = candidate.parsed.observations
        if append_style_conditioning:
            tiled_style = np.repeat(style_vector.reshape(1, -1), candidate.parsed.step_count, axis=0)
            conditioned_obs = np.concatenate(
                [candidate.parsed.observations, tiled_style],
                axis=1,
            ).astype(np.float32)

        style_index = np.full(
            (candidate.parsed.step_count,),
            style_order.index(candidate.style),
            dtype=np.int64,
        )

        valid_files.append(
            DemoFileSummary(
                path=str(candidate.path),
                style=candidate.style,
                size_bytes=candidate.size_bytes,
                step_count=candidate.parsed.step_count,
                raw_observation_size=candidate.parsed.raw_observation_size,
                conditioned_observation_size=int(conditioned_obs.shape[1]),
                continuous_action_size=candidate.parsed.continuous_action_size,
                declared_discrete_branch_size=candidate.parsed.declared_discrete_branch_size,
                effective_discrete_branch_size=candidate.effective_branch_size,
                max_raw_discrete_action=candidate.parsed.observed_max_discrete_action,
            )
        )

        combined_obs.append(candidate.parsed.observations)
        combined_conditioned_obs.append(conditioned_obs)
        combined_cont.append(candidate.parsed.continuous_actions)
        combined_disc.append(candidate.remapped_discrete)
        combined_one_hot.append(discrete_one_hot)
        combined_style_index.append(style_index)

        per_style = per_style_buffers[candidate.style]
        per_style["raw_obs"].append(candidate.parsed.observations)
        per_style["conditioned_obs"].append(conditioned_obs)
        per_style["continuous"].append(candidate.parsed.continuous_actions)
        per_style["discrete"].append(candidate.remapped_discrete)
        per_style["one_hot"].append(discrete_one_hot)
        per_style["style_index"].append(style_index)

    style_file_counts = {
        style_name: sum(1 for item in valid_files if item.style == style_name)
        for style_name in style_order
    }
    style_step_counts = {
        style_name: sum(item.step_count for item in valid_files if item.style == style_name)
        for style_name in style_order
    }
    missing_styles = [
        style_name for style_name in style_order if style_file_counts[style_name] == 0
    ]
    if missing_styles:
        raise ValueError(
            "The chosen dataset schema does not contain at least one valid demo for every style. "
            f"Missing styles: {missing_styles}."
        )

    raw_obs_size = target_raw_obs_size if valid_files else None
    cont_size = valid_files[0].continuous_action_size if valid_files else None
    effective_branch_size = valid_files[0].effective_discrete_branch_size if valid_files else None
    conditioned_obs_size = None
    critic_input_size = None
    if raw_obs_size is not None:
        conditioned_obs_size = raw_obs_size + (len(style_order) if append_style_conditioning else 0)
    if conditioned_obs_size is not None and cont_size is not None and effective_branch_size is not None:
        critic_input_size = conditioned_obs_size + cont_size + effective_branch_size

    summary = DatasetSummary(
        style_order=style_order,
        valid_files=valid_files,
        skipped_files=skipped_files,
        total_steps=sum(item.step_count for item in valid_files),
        raw_observation_size=raw_obs_size,
        conditioned_observation_size=conditioned_obs_size,
        continuous_action_size=cont_size,
        effective_discrete_branch_size=effective_branch_size,
        critic_input_size=critic_input_size,
        style_file_counts=style_file_counts,
        style_step_counts=style_step_counts,
        available_raw_observation_sizes=sorted(schema_report.keys()),
        schema_report=schema_report,
    )

    if output_dir is not None and combined_conditioned_obs:
        output_dir.mkdir(parents=True, exist_ok=True)

        combined_payload = {
            "raw_observations": np.concatenate(combined_obs, axis=0),
            "observations": np.concatenate(combined_conditioned_obs, axis=0),
            "continuous_actions": np.concatenate(combined_cont, axis=0),
            "discrete_actions": np.concatenate(combined_disc, axis=0),
            "discrete_one_hot": np.concatenate(combined_one_hot, axis=0),
            "style_index": np.concatenate(combined_style_index, axis=0),
            "critic_inputs": concatenate_critic_inputs(
                np.concatenate(combined_conditioned_obs, axis=0),
                np.concatenate(combined_cont, axis=0),
                np.concatenate(combined_one_hot, axis=0),
            ),
        }
        save_npz(output_dir / "skeleton_multigail_dataset.npz", combined_payload)

        for style_name, payload_lists in per_style_buffers.items():
            if not payload_lists["conditioned_obs"]:
                continue
            payload = {
                "raw_observations": np.concatenate(payload_lists["raw_obs"], axis=0),
                "observations": np.concatenate(payload_lists["conditioned_obs"], axis=0),
                "continuous_actions": np.concatenate(payload_lists["continuous"], axis=0),
                "discrete_actions": np.concatenate(payload_lists["discrete"], axis=0),
                "discrete_one_hot": np.concatenate(payload_lists["one_hot"], axis=0),
                "style_index": np.concatenate(payload_lists["style_index"], axis=0),
                "critic_inputs": concatenate_critic_inputs(
                    np.concatenate(payload_lists["conditioned_obs"], axis=0),
                    np.concatenate(payload_lists["continuous"], axis=0),
                    np.concatenate(payload_lists["one_hot"], axis=0),
                ),
            }
            save_npz(output_dir / f"{style_name}_dataset.npz", payload)

        manifest = {
            **asdict(summary),
            "append_style_conditioning": append_style_conditioning,
            "chosen_raw_observation_size": target_raw_obs_size,
            "discrete_remap": discrete_remap,
            "output_files": {
                "combined": str(output_dir / "skeleton_multigail_dataset.npz"),
                "per_style": {
                    style_name: str(output_dir / f"{style_name}_dataset.npz")
                    for style_name in style_order
                    if per_style_buffers[style_name]["conditioned_obs"]
                },
            },
        }
        (output_dir / "manifest.json").write_text(json.dumps(manifest, indent=2))

    return summary


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Load skeleton style demonstrations into MultiGAIL-ready datasets."
    )
    parser.add_argument(
        "--demo-root",
        type=Path,
        default=Path("config/SkeletonDemos"),
        help="Folder containing the recorded skeleton .demo files.",
    )
    parser.add_argument(
        "--style",
        action="append",
        default=[],
        help="Style mapping in the form name=filename_prefix. Default: aggressive=SkeletonAggressi, defensive=SkeletonDefensiv",
    )
    parser.add_argument(
        "--min-bytes",
        type=int,
        default=256,
        help="Skip demo files smaller than this many bytes before parsing.",
    )
    parser.add_argument(
        "--expected-raw-observation-size",
        type=int,
        default=None,
        help="Optional raw observation size to force. If omitted, the loader picks the largest schema shared across all requested styles.",
    )
    parser.add_argument(
        "--expected-continuous-size",
        type=int,
        default=2,
        help="Continuous action size to enforce across all loaded demos. Default matches the skeleton runtime.",
    )
    parser.add_argument(
        "--expected-discrete-branch-size",
        type=int,
        default=9,
        help="Effective single-branch discrete action size after remapping. Default matches the current skeleton runtime.",
    )
    parser.add_argument(
        "--remap-discrete",
        action="append",
        default=["9=0"],
        help="Discrete action remap in the form from=to. Default remaps the legacy retreat action 9 to no-op 0.",
    )
    parser.add_argument(
        "--append-style-conditioning",
        action=argparse.BooleanOptionalAction,
        default=True,
        help="Append a one-hot style vector to each observation row before export.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=None,
        help="Optional directory to write .npz datasets and a manifest.json summary.",
    )
    return parser


def print_summary(summary: DatasetSummary) -> None:
    print("Loaded MultiGAIL demo summary")
    print(f"  styles: {summary.style_order}")
    print(f"  valid files: {len(summary.valid_files)}")
    print(f"  skipped files: {len(summary.skipped_files)}")
    print(f"  total steps: {summary.total_steps}")
    print(f"  available raw observation sizes: {summary.available_raw_observation_sizes}")
    print(f"  chosen raw observation size: {summary.raw_observation_size}")
    print(f"  conditioned observation size: {summary.conditioned_observation_size}")
    print(f"  continuous action size: {summary.continuous_action_size}")
    print(f"  effective discrete branch size: {summary.effective_discrete_branch_size}")
    print(f"  critic input size: {summary.critic_input_size}")

    if summary.style_order:
        print("\nStyle coverage:")
        for style_name in summary.style_order:
            print(
                f"  {style_name}: {summary.style_file_counts[style_name]} files, "
                f"{summary.style_step_counts[style_name]} rows"
            )

    if summary.schema_report:
        print("\nSchema candidates:")
        for obs_size, report in sorted(summary.schema_report.items()):
            style_bits = ", ".join(
                f"{style_name}={report['styles'][style_name]['files']} files/{report['styles'][style_name]['steps']} rows"
                for style_name in summary.style_order
            )
            print(
                f"  obs={obs_size}: {report['total_files']} files, {report['total_steps']} rows "
                f"({style_bits})"
            )

    if summary.valid_files:
        print("\nValid files:")
        for item in summary.valid_files:
            print(
                f"  [{item.style}] {Path(item.path).name}: {item.step_count} rows, "
                f"obs={item.raw_observation_size}, cont={item.continuous_action_size}, "
                f"disc={item.effective_discrete_branch_size} "
                f"(declared {item.declared_discrete_branch_size}, max raw {item.max_raw_discrete_action})"
            )

    if summary.skipped_files:
        print("\nSkipped files:")
        for item in summary.skipped_files:
            print(f"  {Path(item.path).name}: {item.reason}")


def main() -> int:
    parser = build_arg_parser()
    args = parser.parse_args()

    style_prefixes = parse_style_args(args.style)
    discrete_remap = parse_remap_args(args.remap_discrete)
    summary = load_dataset(
        demo_root=args.demo_root,
        style_prefixes=style_prefixes,
        min_bytes=args.min_bytes,
        append_style_conditioning=args.append_style_conditioning,
        expected_obs_size=args.expected_raw_observation_size,
        expected_continuous_size=args.expected_continuous_size,
        expected_discrete_branch_size=args.expected_discrete_branch_size,
        discrete_remap=discrete_remap,
        output_dir=args.output_dir,
    )
    print_summary(summary)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
