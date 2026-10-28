using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

/// <summary>
/// Text-to-speech for the minigames (the browser's speechSynthesis in the HTML originals). Unity has none, so on
/// Windows one hidden PowerShell process runs the system voices (System.Speech) and reads lines from stdin;
/// it starts with the first word and stops when the game quits. Silent elsewhere or if no voice is installed.
/// Speaks at the game's volume (Sfx.Volume), and not at all when that is 0.
/// </summary>
public static class Speech
{
    static Process process;
    static bool failed;

    // One line per request: cancel|lang|rate|volume 0-100|base64(UTF-8 text). "stop" cancels what is playing.
    const string Script =
        "Add-Type -AssemblyName System.Speech;" +
        "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;$s.SetOutputToDefaultAudioDevice();" +
        "$v=@{};foreach($i in $s.GetInstalledVoices()){if($i.Enabled){$c=$i.VoiceInfo.Culture.TwoLetterISOLanguageName;if(-not $v.ContainsKey($c)){$v[$c]=$i.VoiceInfo}}};" +
        "while($null -ne ($l=[Console]::In.ReadLine())){" +
        "if($l -eq 'stop'){$s.SpeakAsyncCancelAll();continue};" +
        "$p=$l.Split('|');if($p.Length -lt 5){continue};" +
        "if($p[0] -eq '1'){$s.SpeakAsyncCancelAll()};" +
        "try{$s.Volume=[Math]::Max(0,[Math]::Min(100,[int]$p[3]))}catch{};" +
        "$t=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[4]));" +
        "$b=New-Object System.Speech.Synthesis.PromptBuilder;" +
        "$r=[int]$p[2];$rate=if($r -le -3){'ExtraSlow'}elseif($r -lt 0){'Slow'}else{'Medium'};" +
        "$st=New-Object System.Speech.Synthesis.PromptStyle;$st.Rate=[System.Speech.Synthesis.PromptRate]::$rate;" +
        "if($v.ContainsKey($p[1])){$b.StartVoice($v[$p[1]])};$b.StartStyle($st);$b.AppendText($t);$b.EndStyle();" +
        "if($v.ContainsKey($p[1])){$b.EndVoice()};" +
        "[void]$s.SpeakAsync($b)}";

    /// <summary>Speak text in a language ("en", "ja"). cancel stops anything still playing first.</summary>
    /// <param name="rate">Below 0 is slower: -1 slow, -3 extra slow.</param>
    public static void Say(string text, string lang, int rate = 0, bool cancel = true)
    {
        if (string.IsNullOrEmpty(text) || !Ensure()) return;
        // the voice plays outside Unity, so AudioListener.volume doesn't reach it: send the game volume along
        int volume = Mathf.RoundToInt(Sfx.Volume * 100f);
        if (volume <= 0) return;
        Send((cancel ? "1" : "0") + "|" + lang + "|" + rate + "|" + volume + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
    }

    public static void Stop()
    {
        if (process != null && !process.HasExited) Send("stop");
    }

    static bool Ensure()
    {
        if (failed) return false;
        if (process != null && !process.HasExited) return true;
        if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
        {
            failed = true;
            return false;
        }
        try
        {
            var info = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            process = Process.Start(info);
            Application.quitting += Shutdown;
            return process != null;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Speech: couldn't start the Windows voice (" + e.Message + "); the quiz stays silent.");
            failed = true;
            return false;
        }
    }

    static void Send(string line)
    {
        try
        {
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }
        catch (Exception)
        {
            failed = true;
        }
    }

    static void Shutdown()
    {
        Application.quitting -= Shutdown;
        try
        {
            if (process != null && !process.HasExited) process.Kill();
        }
        catch (Exception) { }
        process = null;
    }
}
