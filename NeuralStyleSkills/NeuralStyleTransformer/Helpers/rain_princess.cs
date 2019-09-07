// This file was automatically generated by VS extension Windows Machine Learning Code Generator v3
// from model file rain_princess.onnx
// Warning: This file may get overwritten if you add add an onnx file with the same name
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.AI.MachineLearning;
namespace NeuralStyleTransformer
{
    
    public sealed class rain_princessInput
    {
        public TensorFloat input1; // shape(1,3,224,224)
    }
    
    public sealed class rain_princessOutput
    {
        public TensorFloat output1; // shape(1,3,224,224)
    }
    
    public sealed class rain_princessModel
    {
        private LearningModel model;
        private LearningModelSession session;
        private LearningModelBinding binding;
        public static async Task<rain_princessModel> CreateFromStreamAsync(IRandomAccessStreamReference stream)
        {
            rain_princessModel learningModel = new rain_princessModel();
            learningModel.model = await LearningModel.LoadFromStreamAsync(stream);
            learningModel.session = new LearningModelSession(learningModel.model);
            learningModel.binding = new LearningModelBinding(learningModel.session);
            return learningModel;
        }
        public async Task<rain_princessOutput> EvaluateAsync(rain_princessInput input)
        {
            binding.Bind("input1", input.input1);
            var result = await session.EvaluateAsync(binding, "0");
            var output = new rain_princessOutput();
            output.output1 = result.Outputs["output1"] as TensorFloat;
            return output;
        }
    }
}

