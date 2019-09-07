using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NeuralStyleTransformer
{
    public static class NeuralStyleTransformerConst
    {
        //public const string WINML_MODEL_FILENAME = "candy.onnx";
        public const string WINML_MODEL_INPUTNAME = "input1";
        public const string WINML_MODEL_OUTPUTNAME = "output1";
        public const string SKILL_INPUTNAME_IMAGE = "InputImage";
        public const string SKILL_OUTPUTNAME_IMAGE = "OutputImage";
        public const int IMAGE_WIDTH = 224;
        public const int IMAGE_HEIGHT = 224;

        //public const string SKILL_OUTPUTNAME_FACESENTIMENTSCORES = "FaceSentimentScores";
        //public static readonly List<float> ZeroFaceRectangleCoordinates = new List<float> { 0.0f, 0.0f, 0.0f, 0.0f };
        //public static readonly List<float> ZeroFaceSentimentScores = new List<float> { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };
    }
    public enum StyleChoices
    {
        Candy=0,Mosaic,Pointilism, RainPrincess, Udnie
    }
}
