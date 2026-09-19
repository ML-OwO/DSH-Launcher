using System.Runtime.InteropServices;

namespace DSHLauncher.Linux;

internal static class Native
{
    private const string Gtk = "libgtk-3.so.0";
    private const string Indicator = "libappindicator3.so.1";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Activate(IntPtr widget, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Tick(IntPtr data);
    [DllImport(Gtk)] internal static extern int gtk_init_check(IntPtr argc, IntPtr argv);
    [DllImport(Gtk)] internal static extern void gtk_main();
    [DllImport(Gtk)] internal static extern void gtk_main_quit();
    [DllImport(Gtk)] internal static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] internal static extern IntPtr gtk_image_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] internal static extern IntPtr gtk_image_new_from_file(string filename);
    [DllImport(Gtk)] internal static extern void gtk_image_menu_item_set_image(IntPtr item, IntPtr image);
    [DllImport(Gtk)] internal static extern void gtk_image_menu_item_set_always_show_image(IntPtr item, int alwaysShow);
    [DllImport(Gtk)] internal static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] internal static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] internal static extern void gtk_menu_shell_append(IntPtr menu, IntPtr child);
    [DllImport(Gtk)] internal static extern void gtk_menu_item_set_label(IntPtr item, [MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] internal static extern void gtk_widget_set_sensitive(IntPtr item, int enabled);
    [DllImport(Gtk)] internal static extern void gtk_widget_show_all(IntPtr menu);
    [DllImport(Gtk)] internal static extern void gtk_widget_set_visible(IntPtr item, int visible);
    [DllImport(Indicator)] internal static extern IntPtr app_indicator_new(string id, string icon, int category);
    [DllImport(Indicator)] internal static extern void app_indicator_set_status(IntPtr indicator, int status);
    [DllImport(Indicator)] internal static extern void app_indicator_set_menu(IntPtr indicator, IntPtr menu);
    [DllImport(Indicator)] internal static extern void app_indicator_set_title(IntPtr indicator, string title);
    [DllImport("libgobject-2.0.so.0")] internal static extern ulong g_signal_connect_data(IntPtr instance, string signal, Activate callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libglib-2.0.so.0")] internal static extern uint g_timeout_add(uint interval, Tick callback, IntPtr data);
    [DllImport("libc", SetLastError = true)] internal static extern int kill(int pid, int signal);
    [DllImport("libc", SetLastError = true)] internal static extern int flock(int fd, int operation);
}
