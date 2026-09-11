namespace Namespace2Xml.Pipeline;

/// <summary>
/// Indicates that an implementation defect violated an invariant between pipeline steps.
/// </summary>
internal sealed class PipelineInvariantException : InvalidOperationException
{
    /// <summary>Creates an invariant failure with the supplied implementation detail.</summary>
    public PipelineInvariantException(string message)
        : base(message)
    {
    }
}
