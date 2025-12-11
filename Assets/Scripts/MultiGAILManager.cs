using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// --- AI INFERENCE (SENTIS) LIBRARIES ---

using Unity.InferenceEngine;


/// <summary>
/// Manages the MultiGAIL reward calculation using the AI Inference (Sentis) engine.
/// This component loads and runs the style critic models and provides a blended
/// reward signal to any agent that requests it.
/// </summary>
public class MultiGAILManager : MonoBehaviour
{
    [Header("MultiGAIL Critic Models")]
    [Tooltip("The list of ONNX model assets for each persona critic.")]
    public List<ModelAsset> criticModelAssets = new List<ModelAsset>();

    [Header("MultiGAIL Alpha Weights")]
    [Tooltip("The blending weights for each persona. MUST be the same size as the Critic Models list.")]
    [Range(0f, 1f)]
    public List<float> alphaWeights = new List<float>();

    // Sentis objects for running the critic models
    private List<Worker> _critics = new List<Worker>();
    private Model[] _runtimeModels;

    void Start()
    {
        // Initialize the Sentis workers for each critic model
        InitializeCritics();
    }

    private void InitializeCritics()
    {
        if (criticModelAssets.Count != alphaWeights.Count)
        {
            Debug.LogError("CRITICAL: The number of critic models and alpha weights must be the same!", this);
            return;
        }

        // Create a runtime model and a worker for each ONNX model asset
        _runtimeModels = new Model[criticModelAssets.Count];
        for (int i = 0; i < criticModelAssets.Count; i++)
        {
            _runtimeModels[i] = ModelLoader.Load(criticModelAssets[i]);
            // BackendType.GPUCompute is faster, but can fall back to CPU if needed.
            Worker worker = new Worker(_runtimeModels[i], BackendType.GPUCompute);
            _critics.Add(worker);
        }
        Debug.Log($"MultiGAIL Manager initialized with {_critics.Count} critics.");
    }

    /// <summary>
    /// Calculates the blended style reward for a given state and action.
    /// </summary>
    /// <param name="observations">The list of observations for the current state.</param>
    /// <param name="discreteAction">The discrete action taken in the state.</param>
    /// <returns>The calculated style reward.</returns>
    public float CalculateStyleReward(List<float> observations, int discreteAction)
    {
        if (_critics.Count == 0) return 0f;

        // The input tensor shape must match what the critic models were trained on.
        // It's (1, number_of_observations + number_of_actions).
        int obsCount = observations.Count;
        int actionCount = 1; // Assuming one discrete action branch

        using (var inputTensor = new Tensor<float>(new TensorShape(1, obsCount + actionCount)))
        {
            // Fill the tensor with observation data
            for (int i = 0; i < obsCount; i++)
            {
                inputTensor[i] = observations[i];
            }
            // Add the action data
            inputTensor[obsCount] = discreteAction;

            // Calculate the weighted reward from all critics
            float totalStyleReward = 0f;
            float totalAlpha = alphaWeights.Sum();
            if (totalAlpha == 0) totalAlpha = 1; // Avoid division by zero

            for (int i = 0; i < _critics.Count; i++)
            {
                // Execute the model
                _critics[i].Schedule(inputTensor);

                // Get the output (the style score)
                var outputTensor = _critics[i].PeekOutput() as Tensor<float>;
                float personaScore = outputTensor[0];

                // Add to the total reward, weighted by its alpha
                totalStyleReward += personaScore * alphaWeights[i];
            }

            // Return the normalized, blended reward
            return totalStyleReward / totalAlpha;
        }
    }

    // Cleanup Sentis workers when the object is destroyed
    void OnDestroy()
    {
        foreach (var worker in _critics)
        {
            worker?.Dispose();
        }
    }
}
