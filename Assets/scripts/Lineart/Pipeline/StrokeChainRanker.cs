using System;
using UnityEngine;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Turns linked stroke elements (SilhouetteStrokeEdge.adj = successor) into ranked chains:
    /// finds every chain's tail with pointer jumping, then computes each element's hop count to the tail.
    /// Drives Resources/Lineart/ComputeShaders/EdgesToStrokes.compute.
    /// </summary>
    public sealed class StrokeChainRanker : IDisposable
    {
        const string ShaderPath = "Lineart/ComputeShaders/EdgesToStrokes";
        static readonly int NumFaces = Shader.PropertyToID("_NumFaces");
        static readonly int StrokesId = Shader.PropertyToID("_strokes");

        readonly ComputeShader _shader;
        readonly int _initialize;
        readonly int _findStrokeTail;
        readonly int _resetNext;
        readonly int _initRanks;
        readonly int _calculateRanks;
        readonly int _finalizeRanks;
        readonly int _sanitizeLinks;

        public StrokeChainRanker()
        {
            _shader = LineartCompute.LoadInstance(ShaderPath);
            _initialize = _shader.FindKernel("Initialize");
            _findStrokeTail = _shader.FindKernel("FindStrokeTail");
            _resetNext = _shader.FindKernel("ResetNextPointer");
            _initRanks = _shader.FindKernel("InitializeRanks");
            _calculateRanks = _shader.FindKernel("CalculateRanks");
            _finalizeRanks = _shader.FindKernel("FinalizeRanks");
            _sanitizeLinks = _shader.FindKernel("SanitizeLinks");
        }

        /// <summary>Tail discovery + ranking in one go.</summary>
        public void Run(SilhouetteBufferContext buffers, int elementCount, int iterations)
        {
            FindTails(buffers, elementCount, iterations);
            Rank(buffers, elementCount, iterations);
        }

        /// <summary>Sets SilhouetteStrokeEdge.minPoint of every element to its chain's tail.</summary>
        public void FindTails(SilhouetteBufferContext buffers, int elementCount, int iterations)
        {
            int groups = LineartCompute.ThreadGroups(elementCount);
            if (groups == 0) return;
            _shader.SetInt(NumFaces, elementCount);

            Dispatch(_sanitizeLinks, buffers, groups);
            Dispatch(_initialize, buffers, groups);
            buffers.SwapNextPointers();

            for (int i = 0; i < iterations; ++i)
            {
                Dispatch(_findStrokeTail, buffers, groups);
                buffers.SwapNextPointers();
            }
        }

        /// <summary>Computes SilhouetteStrokeEdge.rank (hops to tail), cyclic and child flags.</summary>
        public void Rank(SilhouetteBufferContext buffers, int elementCount, int iterations)
        {
            int groups = LineartCompute.ThreadGroups(elementCount);
            if (groups == 0) return;
            _shader.SetInt(NumFaces, elementCount);

            Dispatch(_resetNext, buffers, groups);
            buffers.SwapNextPointers();

            Dispatch(_initRanks, buffers, groups);
            buffers.SwapRanks();

            for (int i = 0; i < iterations; ++i)
            {
                Dispatch(_calculateRanks, buffers, groups);
                buffers.SwapNextPointers();
                buffers.SwapRanks();
            }

            Dispatch(_finalizeRanks, buffers, groups);
        }

        void Dispatch(int kernel, SilhouetteBufferContext buffers, int groups)
        {
            _shader.SetBuffer(kernel, StrokesId, buffers.Strokes);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerSrc, buffers.NextPointerSrc);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.NextPointerDst, buffers.NextPointerDst);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.RankSrc, buffers.RankSrc);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.RankDst, buffers.RankDst);
            _shader.Dispatch(kernel, groups, 1, 1);
        }

        public void Dispose() => LineartCompute.Release(_shader);
    }
}
