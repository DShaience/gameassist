using System.Threading;
using System.Threading.Tasks;

namespace GameScreenTranslator.Core.Abstractions;

public interface ITtsService
{
    Task SpeakAsync(string text, string targetLanguage, CancellationToken cancellationToken = default);

    Task StopAsync();
}
