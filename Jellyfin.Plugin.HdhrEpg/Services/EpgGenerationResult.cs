using System.Collections.Generic;

namespace Jellyfin.Plugin.HdhrEpg.Services;

/// <summary>
/// The outcome of an <see cref="HdhrEpgGenerator.GenerateAsync"/> run.
/// </summary>
/// <param name="ChannelCount">Channels written to the merged XMLTV file.</param>
/// <param name="ProgramCount">Programmes written to the merged XMLTV file.</param>
/// <param name="Failures">Devices that failed, with the reason, if any. The run only throws
/// when every device fails; a non-empty list here alongside a successful result means at
/// least one device succeeded and the merged file reflects that partial data.</param>
public sealed record EpgGenerationResult(int ChannelCount, int ProgramCount, IReadOnlyList<(string Host, string Error)> Failures);
