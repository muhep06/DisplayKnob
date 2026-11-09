namespace DisplayKnob.Core.Audio;

/// <summary>
/// A Windows audio render endpoint.
/// </summary>
/// <param name="Id">Opaque endpoint ID (stable across reboots).</param>
/// <param name="FriendlyName">Name shown in Windows, e.g. <c>S34C65xU (NVIDIA High Definition Audio)</c>.</param>
public sealed record AudioEndpoint(string Id, string FriendlyName);
