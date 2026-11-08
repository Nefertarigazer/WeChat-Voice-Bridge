using System.Linq;
using VoiceBridge.Core;
using VoiceBridge.FishAudio;

namespace VoiceBridge.App;

public partial class MainWindow
{
    private bool applyingProvider;

    private bool RefreshContactSpeech(ContactCard card)
    {
        var voice = library.FirstOrDefault(item => item.Id == card.Profile.VoiceProfileId);
        var provider = providers.FirstOrDefault(item => item.Id == voice?.ProviderId);
        return card.RefreshSpeechOptions(provider, providers.Count > 1);
    }

    private void StoreProvider(SpeechProviderProfile updated)
    {
        var index = providers.FindIndex(item => item.Id == updated.Id);
        if (index < 0) providers.Add(updated); else providers[index] = updated;
        if (updated.Id == DefaultFishId)
        {
            // Publish only after the default connection and legacy controls agree.
            applyingProvider = true;
            try
            {
                SelectKey(updated.CredentialId); RefreshKeys(updated.CredentialId);
                fishModel = updated.Model;
                PaidModel.IsChecked = fishModel == FishSpeech.PaidModel;
                FreeModel.IsChecked = fishModel != FishSpeech.PaidModel;
                LatencyBox.SelectedItem = updated.Latency;
            }
            finally { applyingProvider = false; }
        }
        speechFactory.ResetFailures(updated.Id);
    }
}
