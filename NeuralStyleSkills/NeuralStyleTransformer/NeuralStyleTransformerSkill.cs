// Copyright (c) Microsoft Corporation. All rights reserved. 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.AI.Skills.SkillInterfacePreview;
using Windows.AI.MachineLearning;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.FaceAnalysis;
using Windows.Storage;

namespace NeuralStyleTransformer
{
    /// <summary>
    /// NeuralStyleTransformerSkill class.
    /// Contains the main execution logic of the skill, findind a face in an image then running an ML model to infer its sentiment
    /// Also acts as a factory for NeuralStyleTransformerBinding
    /// to obtain the ONNX model used in this skill, refer to https://github.com/onnx/models/tree/master/emotion_ferplus
    /// </summary>
    public sealed class NeuralStyleTransformerSkill : ISkill
    {
        // WinML related members
        private LearningModelSession m_winmlSession = null;
        static VideoFrame _outputFrame = null;
        /// <summary>
        /// Creates and initializes a NeuralStyleTransformerSkill instance
        /// </summary>
        /// <param name="descriptor"></param>
        /// <param name="device"></param>
        /// <returns></returns>
        internal static IAsyncOperation<NeuralStyleTransformerSkill> CreateAsync(
            ISkillDescriptor descriptor,
            ISkillExecutionDevice device, StyleChoices Mode)
        {
            return AsyncInfo.Run(async (token) =>
            {
                // Create instance
                var skillInstance = new NeuralStyleTransformerSkill(descriptor, device);

                // Load WinML model
                var modelName = "candy.onnx";
                switch (Mode)
                {
                    case StyleChoices.Candy:
                        modelName = "candy.onnx";
                        break;
                    case StyleChoices.Mosaic:
                        modelName = "mosaic.onnx";
                        break;
                    case StyleChoices.Pointilism:
                        modelName = "pointilism.onnx";
                        break;
                    case StyleChoices.RainPrincess:
                        modelName = "rain_princess.onnx";
                        break;
                    case StyleChoices.Udnie:
                        modelName = "udnie.onnx";
                        break;
                }
                var modelFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///NeuralStyleTransformer/Models/{modelName}"));
                var winmlModel = LearningModel.LoadFromFilePath(modelFile.Path);

                // Create WinML session
                skillInstance.m_winmlSession = new LearningModelSession(winmlModel, GetWinMLDevice(device));
                // Create output frame
                _outputFrame?.Dispose();
                _outputFrame = new VideoFrame(BitmapPixelFormat.Bgra8, NeuralStyleTransformerConst.IMAGE_WIDTH, NeuralStyleTransformerConst.IMAGE_HEIGHT);
                return skillInstance;
            });
        }

        /// <summary>
        /// NeuralStyleTransformerSkill constructor
        /// </summary>
        /// <param name="description"></param>
        /// <param name="device"></param>
        private NeuralStyleTransformerSkill(
            ISkillDescriptor description,
            ISkillExecutionDevice device)
        {
            SkillDescriptor = description;
            Device = device;
        }

        /// <summary>
        /// Factory method for instantiating NeuralStyleTransformerBinding
        /// </summary>
        /// <returns></returns>
        public IAsyncOperation<ISkillBinding> CreateSkillBindingAsync()
        {
            return AsyncInfo.Run((token) =>
            {
                var completedTask = new TaskCompletionSource<ISkillBinding>();
                completedTask.SetResult(new NeuralStyleTransformerBinding(SkillDescriptor, Device, m_winmlSession));
                return completedTask.Task;
            });
        }

        /// <summary>
        /// Runs the skill against a binding object, executing the skill logic on the associated input features and populating the output ones
        /// This skill proceeds in 2 steps: 
        /// 1) Run FaceDetector against the image and populate the face bound feature in the binding object
        /// 2) If a face was detected, proceeds with sentiment analysis of that portion fo the image using Windows ML then updating the score 
        /// of each possible sentiment returned as result
        /// </summary>
        /// <param name="binding"></param>
        /// <returns></returns>
        public IAsyncAction EvaluateAsync(ISkillBinding binding)
        {
            NeuralStyleTransformerBinding bindingObj = binding as NeuralStyleTransformerBinding;
            if (bindingObj == null)
            {
                throw new ArgumentException("Invalid ISkillBinding parameter: This skill handles evaluation of NeuralStyleTransformerBinding instances only");
            }

            return AsyncInfo.Run(async (token) =>
            {
                // Retrieve input frame from the binding object
                VideoFrame inputFrame = (binding[NeuralStyleTransformerConst.SKILL_INPUTNAME_IMAGE].FeatureValue as SkillFeatureImageValue).VideoFrame;
                SoftwareBitmap softwareBitmapInput = inputFrame.SoftwareBitmap;

                // Retrieve a SoftwareBitmap to run face detection
                if (softwareBitmapInput == null)
                {
                    if (inputFrame.Direct3DSurface == null)
                    {
                        throw (new ArgumentNullException("An invalid input frame has been bound"));
                    }
                    softwareBitmapInput = await SoftwareBitmap.CreateCopyFromSurfaceAsync(inputFrame.Direct3DSurface);
                }

                // Retrieve face rectangle feature from the binding object
                var transformedImage = binding[NeuralStyleTransformerConst.SKILL_OUTPUTNAME_IMAGE];

                // Bind the WinML input frame with the adequate face bounds specified as metadata
                bindingObj.m_winmlBinding.Bind(
                    NeuralStyleTransformerConst.WINML_MODEL_INPUTNAME, // WinML feature name
                    inputFrame);
                ImageFeatureValue outputImageFeatureValue = ImageFeatureValue.CreateFromVideoFrame(_outputFrame);
                bindingObj.m_winmlBinding.Bind(NeuralStyleTransformerConst.WINML_MODEL_OUTPUTNAME, outputImageFeatureValue);
                // Run WinML evaluation
                var winMLEvaluationResult = await m_winmlSession.EvaluateAsync(bindingObj.m_winmlBinding, "0");
                // Parse result
                IReadOnlyDictionary<string, object> outputs = winMLEvaluationResult.Outputs;
                foreach (var output in outputs)
                {
                    Debug.WriteLine($"{output.Key} : {output.Value} -> {output.Value.GetType()}");
                }
                //var winMLModelResult = (winMLEvaluationResult.Outputs[NeuralStyleTransformerConst.WINML_MODEL_OUTPUTNAME] as TensorFloat).GetAsVectorView();
                //var predictionScores = SoftMax(winMLModelResult);
                /*
                var result = new VideoFrame(BitmapPixelFormat.Bgra8,
                                                NeuralStyleTransformerConst.IMAGE_WIDTH,
                                                NeuralStyleTransformerConst.IMAGE_HEIGHT,
                                                BitmapAlphaMode.Premultiplied);

                await _outputFrame.CopyToAsync(result);*/
                await transformedImage.SetFeatureValueAsync(_outputFrame);

            });
        }

        /// <summary>
        /// Returns the descriptor of this skill
        /// </summary>
        public ISkillDescriptor SkillDescriptor { get; private set; }

        /// <summary>
        /// Return the execution device with which this skill was initialized
        /// </summary>
        public ISkillExecutionDevice Device { get; private set; }

        /// <summary>
        /// Calculates SoftMax normalization over a set of data
        /// </summary>
        /// <param name="inputs"></param>
        /// <returns></returns>
        private List<float> SoftMax(IReadOnlyList<float> inputs)
        {
            List<float> inputsExp = new List<float>();
            float inputsExpSum = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                inputsExp.Add((float)Math.Exp(input));
                inputsExpSum += inputsExp[i];
            }
            inputsExpSum = inputsExpSum == 0 ? 1 : inputsExpSum;
            for (int i = 0; i < inputs.Count; i++)
            {
                inputsExp[i] /= inputsExpSum;
            }
            return inputsExp;
        }

        /// <summary>
        /// If possible, retrieves a WinML LearningModelDevice that corresponds to an ISkillExecutionDevice
        /// </summary>
        /// <param name="executionDevice"></param>
        /// <returns></returns>
        private static LearningModelDevice GetWinMLDevice(ISkillExecutionDevice executionDevice)
        {
            switch (executionDevice.ExecutionDeviceKind)
            {
                case SkillExecutionDeviceKind.Cpu:
                    return new LearningModelDevice(LearningModelDeviceKind.Cpu);

                case SkillExecutionDeviceKind.Gpu:
                    {
                        var gpuDevice = executionDevice as SkillExecutionDeviceDirectX;
                        return LearningModelDevice.CreateFromDirect3D11Device(gpuDevice.Direct3D11Device);
                    }

                default:
                    throw new ArgumentException("Passing unsupported SkillExecutionDeviceKind");
            }
        }
    }
}
