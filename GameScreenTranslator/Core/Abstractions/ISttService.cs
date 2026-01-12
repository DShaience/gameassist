using System.Threading;
using System.Threading.Tasks;

namespace GameScreenTranslator.Core.Abstractions;

public interface ISttService
{
    Task<string> RecognizeOnceWithEndSilenceTimeoutAsync(int endSilenceTimeoutMs, CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task<string> StopAsync(CancellationToken cancellationToken);
}
