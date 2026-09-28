using System.Runtime.InteropServices;

namespace Amanatsu.AiChat.UI;

// Windows' native file picker; no extra managed desktop framework is required by the IL2CPP plugin.
internal static class NativeFilePicker
{
    private const int FileBufferLength = 32768;
    private const int OfnExplorer = 0x00080000;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnOverwritePrompt = 0x00000002;

    public static string ChooseCard(string currentCardPath)
    {
        var initialDirectory = !string.IsNullOrWhiteSpace(currentCardPath)
            ? Path.GetDirectoryName(currentCardPath)
            : Path.GetFullPath("UserData/chara/female");
        return Show(false, initialDirectory, L.T("キャラカード (*.png)\0*.png\0\0", "Character card (*.png)\0*.png\0\0"), "png", L.T("会話するキャラカードを選択", "Choose the character card to talk with"));
    }

    public static string OpenPersonality(string directory) =>
        Show(false, directory, L.T("性格ファイル (*.json)\0*.json\0\0", "Personality file (*.json)\0*.json\0\0"), "json", L.T("性格ファイルを読み込む", "Open a personality file"));

    public static string ChooseBackground(string currentPath)
    {
        var directory = !string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath)
            ? Path.GetDirectoryName(currentPath) : Path.GetFullPath("DefaultData/common/bg");
        return Show(false, directory, L.T("背景画像 (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0\0", "Background image (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0\0"), "png", L.T("会話背景の画像を選択", "Choose a background image"));
    }

    public static string SavePersonality(string directory) =>
        Show(true, directory, L.T("性格ファイル (*.json)\0*.json\0\0", "Personality file (*.json)\0*.json\0\0"), "json", L.T("性格ファイルを保存する", "Save the personality file"));

    private static string Show(bool save, string initialDirectory, string filter, string extension, string title)
    {
        if (!Directory.Exists(initialDirectory)) initialDirectory = null;

        var fileBuffer = Marshal.AllocHGlobal(FileBufferLength * sizeof(char));
        var filterBuffer = Marshal.StringToHGlobalUni(filter);
        try
        {
            Marshal.WriteInt16(fileBuffer, 0);
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(),
                Owner = GetActiveWindow(),
                Filter = filterBuffer,
                FilterIndex = 1,
                File = fileBuffer,
                MaxFile = FileBufferLength,
                InitialDirectory = initialDirectory,
                Title = title,
                Flags = OfnExplorer | OfnPathMustExist | OfnNoChangeDir
                    | (save ? OfnOverwritePrompt : OfnFileMustExist),
                DefaultExtension = extension
            };
            if (dialog.Owner == IntPtr.Zero) dialog.Owner = GetForegroundWindow();

            if (save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog))
                return Marshal.PtrToStringUni(fileBuffer);
            var error = CommDlgExtendedError();
            if (error == 0) return null; // User cancelled the dialog.
            throw new InvalidOperationException(L.T($"ファイル選択ダイアログを開けませんでした (0x{error:X})", $"Could not open the file dialog (0x{error:X})"));
        }
        finally
        {
            Marshal.FreeHGlobal(filterBuffer);
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    private static readonly int[] CustomColors = new int[16];

    public static UnityEngine.Color? ChooseColor(UnityEngine.Color current)
    {
        var custom = Marshal.AllocHGlobal(16 * sizeof(int));
        try
        {
            Marshal.Copy(CustomColors, 0, custom, 16);
            var c = (UnityEngine.Color32)current;
            var dialog = new ChooseColorData
            {
                Size = Marshal.SizeOf<ChooseColorData>(),
                Owner = GetActiveWindow(),
                Rgb = c.r | (c.g << 8) | (c.b << 16),
                CustomColors = custom,
                Flags = CcRgbInit | CcFullOpen
            };
            if (dialog.Owner == IntPtr.Zero) dialog.Owner = GetForegroundWindow();
            if (!ChooseColorW(ref dialog))
            {
                var error = CommDlgExtendedError();
                if (error == 0) return null;
                throw new InvalidOperationException(L.T($"色選択ダイアログを開けませんでした (0x{error:X})", $"Could not open the color dialog (0x{error:X})"));
            }
            Marshal.Copy(custom, CustomColors, 0, 16);
            return new UnityEngine.Color32((byte)(dialog.Rgb & 0xFF), (byte)((dialog.Rgb >> 8) & 0xFF), (byte)((dialog.Rgb >> 16) & 0xFF), 255);
        }
        finally { Marshal.FreeHGlobal(custom); }
    }

    private const int CcRgbInit = 0x1;
    private const int CcFullOpen = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Rgb;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColorW(ref ChooseColorData dialog);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public IntPtr Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        [MarshalAs(UnmanagedType.LPWStr)] public string TemplateName;
        public IntPtr Reserved;
        public int ReservedSize;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
