﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using VRage.Library.Utils;
using VRage.Noise;
using VRageMath;

namespace Sandbox.Engine.Voxels
{
    public struct MyCsgShapePlanetShapeAttributes
    {
        public float NoiseFrequency;
        public int Seed;
        public float Radius;
        public float DeviationScale;
        public float NormalNoiseFrequency;
        public float LayerDeviationNoiseFreqeuncy;
        public int LayerDeviationSeed;
    }

    public struct MyCsgShapePlanetHillAttributes
    {
        public float Treshold;
        public float BlendTreshold;
        public float SizeRatio;
        public float Frequency;
        public int NumNoises;
    }

    class MyCsgShapePlanet : MyCsgShapeBase
    {
        MyCsgShapePlanetShapeAttributes m_shapeAttributes;
        MyCsgShapePlanetHillAttributes m_hillAttributes;
        MyCsgShapePlanetHillAttributes m_canyonAttributes;
       
        private IMyModule m_hillModule;
        private float m_hillHalfDeviation;
        private float m_hillBlendTreshold;

        private IMyModule m_canyonModule;
        private float m_canyonHalfDeviation;
        private float m_canyonBlendTreshold;

        private float m_deviationFrequency;
        private float m_detailFrequency;
        private float m_halfDeviation;
     

        private float m_outerRadius;
        private float m_innerRadius;

        private Vector3 m_translation;

        private IMyModule m_normalModule;

        private MyRandom m_random;


        public MyCsgShapePlanet(MyRandom random, Vector3 translation, ref MyCsgShapePlanetShapeAttributes shapeAttributes, ref MyCsgShapePlanetHillAttributes hillAttributes, ref MyCsgShapePlanetHillAttributes canyonAttributes, float deviationFrequency = 0, float detailFrequency = 0)
        {
            m_translation = translation;
            m_random = random;
            m_shapeAttributes = shapeAttributes;
            m_hillAttributes = hillAttributes;
            m_canyonAttributes = canyonAttributes;

            m_canyonBlendTreshold = m_canyonAttributes.Treshold + m_canyonAttributes.BlendTreshold;
            m_hillBlendTreshold = m_hillAttributes.Treshold - m_hillAttributes.BlendTreshold;

            m_shapeAttributes.Radius = (shapeAttributes.Radius/2.0f) * (1 - shapeAttributes.DeviationScale * m_hillAttributes.SizeRatio);
            m_halfDeviation = (shapeAttributes.Radius / 2.0f) * shapeAttributes.DeviationScale;

            m_deviationFrequency = deviationFrequency;
            m_detailFrequency    = detailFrequency;

            m_hillHalfDeviation = m_halfDeviation * m_hillAttributes.SizeRatio;
            m_canyonHalfDeviation = m_halfDeviation * m_canyonAttributes.SizeRatio;
          
            m_enableModulation = true;

            m_hillModule = new MyCompositeNoise(hillAttributes.NumNoises, hillAttributes.Frequency / m_shapeAttributes.Radius, random);

            m_canyonModule = new MyCompositeNoise(canyonAttributes.NumNoises, canyonAttributes.Frequency / m_shapeAttributes.Radius, random);

            m_normalModule = new MySimplexFast(seed: random.Next(), frequency: shapeAttributes.NormalNoiseFrequency / m_shapeAttributes.Radius);

            ComputeDerivedProperties();
        }

        internal override ContainmentType Contains(ref BoundingBox queryAabb, ref BoundingSphere querySphere, float lodVoxelSize)
        {
            ContainmentType outerContainment, innerContainment;

            BoundingSphere sphere = new BoundingSphere(
                m_translation,
                m_outerRadius + lodVoxelSize);

            sphere.Contains(ref queryAabb, out outerContainment);
            if (outerContainment == ContainmentType.Disjoint)
                return ContainmentType.Disjoint;

            sphere.Radius = m_innerRadius - lodVoxelSize;
            sphere.Contains(ref queryAabb, out innerContainment);
            if (innerContainment == ContainmentType.Contains)
                return ContainmentType.Contains;

            return ContainmentType.Intersects;
        }

        internal override float SignedDistance(ref Vector3 position, float lodVoxelSize, IMyModule macroModulator, IMyModule detailModulator)
        {
            Vector3 localPosition = position - m_translation;
            float distance = localPosition.Length();
            if ((m_innerRadius - lodVoxelSize) > distance)
                return -1f;
            if ((m_outerRadius + lodVoxelSize) < distance)
                return 1f;

            float signedDistance = distance - m_shapeAttributes.Radius;

            Debug.Assert(m_deviationFrequency != 0f);
            float normalizer = m_deviationFrequency * m_shapeAttributes.Radius / distance;
            var tmp = localPosition * normalizer;

            bool changed = false;
            float terrainValue = (float)macroModulator.GetValue(tmp.X, tmp.Y, tmp.Z);
            // Debug.Assert(terrainValue <= 1.0f);
            if (terrainValue > m_hillBlendTreshold)
            {
                changed = true;
                float hillValue = (float)m_hillModule.GetValue(tmp.X, tmp.Y, tmp.Z);

                if (terrainValue > m_hillAttributes.Treshold)
                {
                    signedDistance -= hillValue * m_hillHalfDeviation;
                }
                else
                {
                    float normalValue = (float)m_normalModule.GetValue(tmp.X, tmp.Y, tmp.Z);
                    float blendValue = MathHelper.Saturate((terrainValue - m_hillBlendTreshold) / (m_hillAttributes.Treshold - m_hillBlendTreshold));
                    signedDistance -= MathHelper.Lerp(normalValue * m_halfDeviation, hillValue * m_hillHalfDeviation, blendValue);
                }
            } 
           
            if (terrainValue < m_canyonBlendTreshold)
            {
                changed = true;
                float canoynValue = (float)m_canyonModule.GetValue(tmp.X, tmp.Y, tmp.Z);

                if (terrainValue < m_canyonAttributes.Treshold)
                {
                    signedDistance += canoynValue * m_canyonHalfDeviation;
                }
                else
                {
                    float normalValue = (float)m_normalModule.GetValue(tmp.X, tmp.Y, tmp.Z);
                    float blendValue = MathHelper.Saturate((terrainValue - m_canyonBlendTreshold) / (m_canyonAttributes.Treshold - m_canyonBlendTreshold));
                    signedDistance -= MathHelper.Lerp(normalValue * m_halfDeviation, -canoynValue * m_canyonHalfDeviation, blendValue);
                }
            }

            if(changed == false)
            {
                float normalValue = (float)m_normalModule.GetValue(tmp.X, tmp.Y, tmp.Z);
                signedDistance -= normalValue * m_halfDeviation;
            }

            return signedDistance / lodVoxelSize;
        }

        internal override void DebugDraw(ref Vector3D worldTranslation, Color color)
        {
            VRageRender.MyRenderProxy.DebugDrawSphere(worldTranslation + m_translation, m_shapeAttributes.Radius, color.ToVector3(), alpha: 0.5f, depthRead: true, smooth: false);
        }

        internal override MyCsgShapeBase DeepCopy()
        {
            return new MyCsgShapePlanet(
                m_random,
                m_translation,
                ref m_shapeAttributes,
                ref m_hillAttributes,
                ref m_canyonAttributes,
                m_deviationFrequency,
                m_detailFrequency);
        }

        internal override void ShrinkTo(float percentage)
        {
            m_shapeAttributes.Radius *= percentage;
            m_halfDeviation *= percentage;
            m_hillAttributes.SizeRatio *= percentage;
            m_canyonAttributes.SizeRatio *= percentage;

            m_canyonHalfDeviation = m_halfDeviation * m_canyonAttributes.SizeRatio;
            m_hillHalfDeviation = m_halfDeviation * m_hillAttributes.SizeRatio;

            ComputeDerivedProperties();
        }

        private void ComputeDerivedProperties()
        {
            m_outerRadius = m_shapeAttributes.Radius  + m_hillHalfDeviation + m_detailSize;
            m_innerRadius = m_shapeAttributes.Radius -  m_canyonHalfDeviation - m_detailSize;
        }

        internal override Vector3 Center()
        {
            return m_translation;
        }

        public MyCsgShapePlanetShapeAttributes GetShapeAttributes()
        {
            return m_shapeAttributes;
        }

        public MyCsgShapePlanetHillAttributes GetHillAttributes()
        {
            return m_hillAttributes;
        }

        public MyCsgShapePlanetHillAttributes GetCanyonAttributes()
        {
            return m_canyonAttributes;
        }
    }
}
