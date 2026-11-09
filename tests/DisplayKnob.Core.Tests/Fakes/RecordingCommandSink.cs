using DisplayKnob.Core.Volume;

namespace DisplayKnob.Core.Tests.Fakes;

internal sealed class RecordingCommandSink : IVolumeCommandSink
{
    public List<VolumeCommand> Commands { get; } = [];

    public bool Accept { get; set; } = true;

    public bool TryPost(VolumeCommand command)
    {
        if (Accept)
        {
            Commands.Add(command);
        }

        return Accept;
    }
}
