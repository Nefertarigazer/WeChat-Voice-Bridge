using System.Linq;
using VoiceBridge.Core;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private bool RefreshContactSpeech(ContactCard card)
    {
        var voice = library.FirstOrDefault(item => item.Id == card.Profile.VoiceProfileId);
        var provider = voice is null ? null : ActiveProviderFor(voice, providers);
        return card.RefreshSpeechOptions(provider, providers.Count > 1, voice?.Model ?? "");
    }

    private void StoreProvider(SpeechProviderProfile updated)
    {
        var index = providers.FindIndex(item => item.Id == updated.Id);
        if (index < 0) providers.Add(updated); else providers[index] = updated;
        speechFactory.ResetFailures(updated.Id);
    }
}
