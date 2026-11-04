namespace MoaView;

public static class Program
{
    [System.STAThread]
    public static int Main(string[] args)
    {
        var app = new App(args);
        app.InitializeComponent();
        return app.Run();
    }
}
