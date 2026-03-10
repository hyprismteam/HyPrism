namespace HyPrism.Models;

/// <summary>
/// Status of Rosetta 2 installation on macOS Apple Silicon.
/// </summary>
public class RosettaStatus
{
    public bool NeedsInstall { get; set; }
    public string Message { get; set; } = "";
    public string Command { get; set; } = "";
    public string? TutorialUrl { get; set; }
}
