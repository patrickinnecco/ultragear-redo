using System.Collections.Generic;
using System.Linq;

namespace Ultragear.Core
{
    /// <summary>
    /// Command map for the LG 27GX704A (EDID GSM5CEA), measured on the real monitor by observing
    /// which DDC/CI VCP code/value pairs are sent and read back for each control (see ANALISE.md).
    /// </summary>
    public static class Lg27GX704A
    {
        public const byte Brightness = 0x10;
        public const byte Contrast = 0x12;
        public const byte ColorPreset = 0x14;       // Warm / Medium / Cool / User / Manual
        public const byte PictureMode = 0x15;       // LG game/picture mode
        public const byte RedGain = 0x16;
        public const byte GreenGain = 0x18;
        public const byte BlueGain = 0x1A;
        public const byte Volume = 0x62;            // headphone output level
        public const byte ColorTempKelvin = 0x69;   // used when ColorPreset == Manual
        public const byte Sharpness = 0x87;
        public const byte AudioMute = 0x8D;         // 1 = mute, 2 = sound on
        public const byte UsageTime = 0xC0;
        public const byte FirmwareLevel = 0xC9;
        public const byte VcpVersion = 0xDF;
        public const byte ResponseTime = 0xF7;      // 255 = not supported on this model
        public const byte AdaptiveSync = 0xF8;      // 0 = Off, 1 = On (monitor re-syncs the signal)
        public const byte BlackStabilizer = 0xF9;   // read 0..100, write in steps of 5 (value / 5)
        public const byte SmartEnergySaving = 0xF6; // 0 = Desligado, 1 = Baixo, 2 = Elevado (2 confirmed in the OSD)
        // Not writable on this model (verified): 0xFE gamma preset (write ignored), 0xF7 response time (reports 255).

        public const int KelvinMin = 4000, KelvinMax = 10000, KelvinStep = 100;
        public const uint ManualPresetValue = 0xFFDE;

        public static readonly IReadOnlyList<ColorTempOption> ColorTemps = new[]
        {
            new ColorTempOption("Quente", 3),
            new ColorTempOption("Médio", 5),
            new ColorTempOption("Frio", 9),
            new ColorTempOption("Personalizada (R/G/B)", 11),
            new ColorTempOption("Manual (Kelvin)", ManualPresetValue),
        };

        public static ColorTempOption ColorTempFor(uint raw) => ColorTemps.FirstOrDefault(c => c.Value == raw);

        /// <summary>Picture modes in the order LG Switch shows them. Codes were confirmed by read-back on the monitor.</summary>
        public static readonly List<PictureModeInfo> Modes = new()
        {
            new PictureModeInfo(65, "Imagem Personalizada", "IconPersonalized", "Modo ajustado pelo Assistente de Imagem Personalizada."),
            new PictureModeInfo(45, "Jogador 1", "IconGamepad", "Guarda um conjunto próprio de ajustes de jogo."),
            new PictureModeInfo(46, "Jogador 2", "IconGamepad", "Segundo conjunto de ajustes de jogo."),
            new PictureModeInfo(18, "Calibração 1", "IconCalibration", "Resultado de calibração por hardware (somente SDR)."),
            new PictureModeInfo(19, "Calibração 2", "IconCalibration", "Resultado de calibração por hardware (somente SDR)."),
            new PictureModeInfo(6,  "Fraqueza de cor", "IconColorWeak", "Ajuda a distinguir vermelho e verde (somente SDR)."),
            new PictureModeInfo(25, "DCI-P3", "IconVideo", "Gama DCI-P3 para vídeo digital (somente SDR)."),
            new PictureModeInfo(30, "FPS", "IconCrosshair", "Otimizado para jogos de tiro escuros."),
        };

        /// <summary>
        /// Modes present in the monitor OSD (manual) but not shown by LG Switch for this model.
        /// Codes confirmed by setting them over DDC/CI and reading the mode name in the monitor OSD (2026-11-09).
        /// Code 48 is accepted too but the OSD shows it as FPS (internal variant) — deliberately not listed.
        /// </summary>
        public static readonly List<PictureModeInfo> ExtraModes = new()
        {
            new PictureModeInfo(31, "RTS", "IconRts", "Otimizado para jogos de estratégia em tempo real."),
            new PictureModeInfo(49, "Vívida", "IconVivid", "Cores mais vivas."),
            new PictureModeInfo(1,  "Leitor", "IconReader", "Otimizado para leitura de documentos (somente SDR)."),
            new PictureModeInfo(39, "Efeito HDR", "IconHdr", "Simula alta faixa dinâmica em conteúdo SDR (somente SDR)."),
            new PictureModeInfo(15, "sRGB", "IconSrgb", "Padrão de cores sRGB (somente SDR)."),
        };

        public static IEnumerable<PictureModeInfo> AllModes => Modes.Concat(ExtraModes);
        public static PictureModeInfo ModeFor(uint code) => AllModes.FirstOrDefault(m => m.Code == code);
        public static PictureModeInfo ModeByName(string name) => AllModes.FirstOrDefault(m => m.Name == name);
    }

    public sealed class ColorTempOption
    {
        public ColorTempOption(string name, uint value) { Name = name; Value = value; }
        public string Name { get; }
        public uint Value { get; }
        public override string ToString() => Name;
    }

    public sealed class PictureModeInfo
    {
        public PictureModeInfo(uint code, string name, string icon, string description)
        { Code = code; Name = name; Icon = icon; Description = description; }
        public uint Code { get; }
        public string Name { get; }
        public string Icon { get; }
        public string Description { get; }
        public override string ToString() => Name;
    }
}
