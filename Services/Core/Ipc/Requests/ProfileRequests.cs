namespace HyPrism.Services.Core.Ipc.Requests;

public record CreateProfileRequest(string Name, string Uuid, bool? IsOfficial = null);
public record SwitchProfileRequest(string Id);
