using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Japanese / English. L.T("日本語", "English") picks by the current language; L.Tr(text) translates texts that
/// live in scenes (quest texts, button labels) through the table below and leaves anything unknown as it is.
/// The choice is remembered between sessions (PlayerPrefs) and survives 最初から; the level-1 menu toggles it.
/// UI that stays on screen listens to Changed and redraws.
/// </summary>
public static class L
{
    const string PrefKey = "language";
    static bool? english;

    public static bool English
    {
        get
        {
            if (english == null) english = PlayerPrefs.GetInt(PrefKey, 0) == 1;
            return english.Value;
        }
        set
        {
            if (English == value) return;
            english = value;
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Raised after the language changes.</summary>
    public static event Action Changed;

    public static void Toggle() => English = !English;

    public static string T(string ja, string en) => English ? en : ja;

    /// <summary>A scene-stored Japanese text in the current language (unchanged if there's no translation).
    /// Quest texts also have a shorter Japanese wording, used instead of the one saved in the scene.</summary>
    public static string Tr(string ja)
    {
        if (string.IsNullOrEmpty(ja)) return ja;
        if (!English) return Short.TryGetValue(ja, out var s) ? s : ja;
        return Table.TryGetValue(ja, out var en) ? en : ja;
    }

    // No domain reload on entering Play Mode: drop listeners left over from the last session
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewSession()
    {
        Changed = null;
        english = null;
    }

    static readonly Dictionary<string, string> Table = new Dictionary<string, string>
    {
        // play zone buttons
        ["▶ プレイ"] = "▶ Play",
        ["２階へ"] = "Up to 2F",
        ["３階へ"] = "Up to 3F",
        ["４階へ"] = "Up to 4F",
        ["５階へ"] = "Up to 5F",
        ["チケットを購入"] = "Buy ticket",
        // quests
        ["エレベーターで2階に行きましょう！"] = "Ride the elevator to 2F!",
        ["３階へのチケットを買いましょう！"] = "Buy a ticket to 3F!",
        ["建築学科の教室でクイズをクリアしましょう！"] = "Clear the quiz in the drafting room!",
        ["スタジオでゲームをクリアしましょう！"] = "Clear the studio game!",
        ["ITエンジニア学科の教室でミニゲームをクリアしよう！"] = "Clear the IT classroom game!",
    };

    // Shorter Japanese for the quest texts saved in the scenes (keys = the scene text)
    static readonly Dictionary<string, string> Short = new Dictionary<string, string>
    {
        ["エレベーターで2階に行きましょう！"] = "エレベーターで２階へ行こう！",
        ["３階へのチケットを買いましょう！"] = "３階へのチケットを買おう！",
        ["建築学科の教室でクイズをクリアしましょう！"] = "建築学科の製図室でクイズをクリアしよう！",
        ["スタジオでゲームをクリアしましょう！"] = "スタジオのゲームをクリアしよう！",
        ["ITエンジニア学科の教室でミニゲームをクリアしよう！"] = "IT学科のゲームをクリアしよう！",
    };
}
