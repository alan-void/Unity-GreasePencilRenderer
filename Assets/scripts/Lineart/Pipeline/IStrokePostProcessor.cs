using DefaultNamespace;

namespace Lineart.Pipeline
{
    /// <summary>
    /// Runs on the dense stroke buffers after an IGreasePencilEdgeCalculator produced them and before they are drawn.
    /// Components implementing this on the same GameObject as a SilhouetteRenderer are executed in component order.
    /// </summary>
    public interface IStrokePostProcessor
    {
        bool isActiveAndEnabled { get; }
        void Process(IGreasePencilEdgeCalculator calculator);
    }
}
