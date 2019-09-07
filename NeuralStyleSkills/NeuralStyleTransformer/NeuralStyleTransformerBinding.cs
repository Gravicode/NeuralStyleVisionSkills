// Copyright (c) Microsoft Corporation. All rights reserved. 

using System.Collections;
using System.Collections.Generic;
using Microsoft.AI.Skills.SkillInterfacePreview;
using Windows.AI.MachineLearning;
using System.Linq;
using Windows.Foundation;
using Windows.Media;

#pragma warning disable CS1591 // Disable missing comment warning

namespace NeuralStyleTransformer
{
    

    /// <summary>
    /// NeuralStyleTransformerBinding class.
    /// It holds the input and output passed and retrieved from a NeuralStyleTransformerSkill instance.
    /// </summary>
    public sealed class NeuralStyleTransformerBinding : IReadOnlyDictionary<string, ISkillFeature>, ISkillBinding
    {
        // WinML related
        internal LearningModelBinding m_winmlBinding = null;
        private VisionSkillBindingHelper m_bindingHelper = null;

        /// <summary>
        /// NeuralStyleTransformerBinding constructor
        /// </summary>
        internal NeuralStyleTransformerBinding(
            ISkillDescriptor descriptor,
            ISkillExecutionDevice device,
            LearningModelSession session)
        {
            m_bindingHelper = new VisionSkillBindingHelper(descriptor, device);

            // Create WinML binding
            m_winmlBinding = new LearningModelBinding(session);
        }

        /// <summary>
        /// Sets the input image to be processed by the skill
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public IAsyncAction SetInputImageAsync(VideoFrame frame)
        {
            return m_bindingHelper.SetInputImageAsync(frame);
        }

        /// <summary>
        /// Returns transformed image
        /// </summary>
        /// <returns></returns>
        public VideoFrame GetTransformedImage
        {
            get
            {
                ISkillFeature feature = null;
                if (m_bindingHelper.TryGetValue(NeuralStyleTransformerConst.SKILL_OUTPUTNAME_IMAGE, out feature))
                {
                    return (feature.FeatureValue as SkillFeatureImageValue).VideoFrame;
                }
                else
                {
                    return null;
                }
            }
        }

        // interface implementation via composition
        #region InterfaceImpl

        // ISkillBinding
        public ISkillExecutionDevice Device => m_bindingHelper.Device;


        // IReadOnlyDictionary
        public bool ContainsKey(string key)
        {
            return m_bindingHelper.ContainsKey(key);
        }

        public bool TryGetValue(string key, out ISkillFeature value)
        {
            return m_bindingHelper.TryGetValue(key, out value);
        }

        public ISkillFeature this[string key] => m_bindingHelper[key];

        public IEnumerable<string> Keys => m_bindingHelper.Keys;

        public IEnumerable<ISkillFeature> Values => m_bindingHelper.Values;

        public int Count => m_bindingHelper.Count;

        public IEnumerator<KeyValuePair<string, ISkillFeature>> GetEnumerator()
        {
            return m_bindingHelper.AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_bindingHelper.AsEnumerable().GetEnumerator();
        }

        #endregion InterfaceImpl
        // end of implementation of interface via composition
    }
}
