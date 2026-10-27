using System;
using UnityEngine;
using Lineart.Silhouette;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Compacts ranked stroke chains into the dense Grease Pencil point layout the renderer draws
    /// (padding, cyclic duplicates, screen positions, lateral shift and screen-space arc length).
    /// Drives Resources/Lineart/ComputeShaders/StrokesToGreasePencil.compute.
    /// </summary>
    public sealed class DenseStrokeBuilder : IDisposable
    {
        const string ShaderPath = "GreasePencilRenderer/Lineart/ComputeShaders/StrokesToGreasePencil";
        static readonly int NumFaces = Shader.PropertyToID("_NumFaces");
        static readonly int DenseCount = Shader.PropertyToID("_DenseCount");
        static readonly int StrokesId = Shader.PropertyToID("_strokes");
        static readonly int StrokeAttribsId = Shader.PropertyToID("_strokeAttribs");
        static readonly int DenseArrayId = Shader.PropertyToID("_denseArray");
        static readonly int DenseAttribsId = Shader.PropertyToID("_denseAttribs");
        static readonly int ColorArrayId = Shader.PropertyToID("_colorArray");
        static readonly int NumStrokesCounterId = Shader.PropertyToID("numStrokesCounter");
        static readonly int NumStrokePointsCounterId = Shader.PropertyToID("numStrokePointsCounter");

        static readonly uint[] Zero = { 0u };

        public struct Settings
        {
            public float radiusMultiplier;
            public float lateralShiftFactor;
            public float lateralShiftConstant;
        }

        readonly ComputeShader _shader;
        readonly int _setStrokeLengthAtTail;
        readonly int _calcStrokeOffsets;
        readonly int _invalidateEntries;
        readonly int _moveToDense;
        readonly int _buildDenseNext;
        readonly int _offsetScreenPositions;
        readonly int _initScreenDistances;
        readonly int _jumpScreenDistances;
        readonly int _finalizeScreenDistances;

        public DenseStrokeBuilder()
        {
            _shader = LineartCompute.LoadInstance(ShaderPath);
            _setStrokeLengthAtTail = _shader.FindKernel("SetStrokeLengthAtTail");
            _calcStrokeOffsets = _shader.FindKernel("CalculateArrayOffsets");
            _invalidateEntries = _shader.FindKernel("InvalidateEntries");
            _moveToDense = _shader.FindKernel("MoveToDenseArray");
            _buildDenseNext = _shader.FindKernel("BuildDenseNextPointers");
            _offsetScreenPositions = _shader.FindKernel("OffsetDenseScreenPositions");
            _initScreenDistances = _shader.FindKernel("InitDenseScreenDistances");
            _jumpScreenDistances = _shader.FindKernel("DensePointerJumpDistances");
            _finalizeScreenDistances = _shader.FindKernel("FinalizeDenseScreenDistances");
        }

        public void Build(SilhouetteBufferContext buffers, int elementCount, in StrokeCameraParams camera,
                          Matrix4x4 objectToWorld, Settings settings)
        {
            int elementGroups = LineartCompute.ThreadGroups(elementCount);
            int denseGroups = LineartCompute.ThreadGroups(buffers.DenseCapacity);
            if (elementGroups == 0 || denseGroups == 0) return;

            _shader.SetInt(NumFaces, elementCount);
            _shader.SetInt(DenseCount, buffers.DenseCapacity);
            camera.Apply(_shader, objectToWorld);
            _shader.SetFloat(SilhouetteShaderIDs.RadiusMultiplier, settings.radiusMultiplier);
            _shader.SetFloat(SilhouetteShaderIDs.LateralShiftFactor, settings.lateralShiftFactor);
            _shader.SetFloat(SilhouetteShaderIDs.LateralShiftConstant, settings.lateralShiftConstant);

            buffers.NumStrokesCounter.SetData(Zero);
            buffers.NumStrokePointsCounter.SetData(Zero);

            Dispatch(_setStrokeLengthAtTail, buffers, elementGroups);
            Dispatch(_calcStrokeOffsets, buffers, elementGroups);
            Dispatch(_invalidateEntries, buffers, denseGroups);
            Dispatch(_moveToDense, buffers, elementGroups);

            Dispatch(_buildDenseNext, buffers, denseGroups);
            buffers.SwapDenseNextPointers();

            Dispatch(_offsetScreenPositions, buffers, denseGroups);

            Dispatch(_initScreenDistances, buffers, denseGroups);
            buffers.SwapDenseUStrokeBuffers();

            int iterations = LineartCompute.PointerJumpIterations(buffers.DenseCapacity);
            for (int i = 0; i < iterations; ++i)
            {
                Dispatch(_jumpScreenDistances, buffers, denseGroups);
                buffers.SwapDenseNextPointers();
                buffers.SwapDenseUStrokeBuffers();
            }

            Dispatch(_finalizeScreenDistances, buffers, denseGroups);
        }

        void Dispatch(int kernel, SilhouetteBufferContext buffers, int groups)
        {
            _shader.SetBuffer(kernel, StrokesId, buffers.Strokes);
            _shader.SetBuffer(kernel, StrokeAttribsId, buffers.StrokeAttribs);
            _shader.SetBuffer(kernel, NumStrokesCounterId, buffers.NumStrokesCounter);
            _shader.SetBuffer(kernel, NumStrokePointsCounterId, buffers.NumStrokePointsCounter);
            _shader.SetBuffer(kernel, DenseArrayId, buffers.DenseStrokes);
            _shader.SetBuffer(kernel, DenseAttribsId, buffers.DenseAttribs);
            _shader.SetBuffer(kernel, ColorArrayId, buffers.Color);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.DenseNextPointerSrc, buffers.DenseNextPointerSrc);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.DenseNextPointerDst, buffers.DenseNextPointerDst);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.DenseUStrokeSrc, buffers.DenseUStrokeSrc);
            _shader.SetBuffer(kernel, SilhouetteShaderIDs.DenseUStrokeDst, buffers.DenseUStrokeDst);
            _shader.Dispatch(kernel, groups, 1, 1);
        }

        public void Dispose() => LineartCompute.Release(_shader);
    }
}
