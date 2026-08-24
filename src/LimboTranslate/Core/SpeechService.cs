using System.Globalization;
using System.Speech.Synthesis;

namespace LimboTranslate.Core;

public sealed class SpeechService : IDisposable
{
    private readonly SpeechSynthesizer? _synthesizer;
    private bool _disposed;

    public SpeechService()
    {
        try
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
        }
        catch
        {
            _synthesizer = null;
        }
    }

    public void Speak(string? text, string? langCode)
    {
        if (_disposed || _synthesizer is null || string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            _synthesizer.SpeakAsyncCancelAll();
            SelectVoice(langCode);
            _synthesizer.SpeakAsync(text);
        }
        catch
        {
        }
    }

    public void Stop()
    {
        if (_disposed || _synthesizer is null)
            return;

        try
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
        catch
        {
        }
    }

    private void SelectVoice(string? langCode)
    {
        if (_synthesizer is null || string.IsNullOrWhiteSpace(langCode) || langCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return;

        string wanted = langCode.Length >= 2 ? langCode[..2] : langCode;

        try
        {
            foreach (InstalledVoice voice in _synthesizer.GetInstalledVoices())
            {
                if (!voice.Enabled)
                    continue;

                CultureInfo? culture = voice.VoiceInfo.Culture;
                if (culture is null)
                    continue;

                if (culture.TwoLetterISOLanguageName.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    _synthesizer.SelectVoice(voice.VoiceInfo.Name);
                    return;
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _synthesizer?.SpeakAsyncCancelAll();
            _synthesizer?.Dispose();
        }
        catch
        {
        }
    }
}
