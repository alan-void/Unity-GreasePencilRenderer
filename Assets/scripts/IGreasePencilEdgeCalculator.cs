using UnityEngine;

namespace DefaultNamespace
{
public interface IGreasePencilEdgeCalculator
{
    public void CalculateEdges();

    /// <summary>Number of dense Grease Pencil points the stroke buffers can hold.</summary>
    public int GetMaximumBufferLength();

    public GraphicsBuffer GetStrokeBuffer();
    public GraphicsBuffer GetColorBuffer();

    /// <summary>Per point StrokePointAttributes, parallel to the stroke buffer.</summary>
    public GraphicsBuffer GetStrokeAttributeBuffer();

    /// <summary>Camera the strokes were generated for.</summary>
    public Camera ViewCamera { get; }
}
}
