using CommandLine;
using OFProxy.classes;
using Spectre.Console;

namespace OFConsole
{
    internal class Program
    {
        private static ProxyController con = new ProxyController();
        private static Boolean proxyFinished = false;

        static void Main(string[] args)
        {

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {

                    if (o.Port != 0)
                    {
                        con.setPort(o.Port);
                    }
                    if (o.ForceClose)
                    {
                        con.Stop();
                        Console.WriteLine(" == Proxy closed == ");
                        Environment.Exit(0);
                    }
                });


            
            Console.Clear();
            Console.WriteLine("");
            AnsiConsole.Write(new FigletText("OF Auth Catcher").Centered().Color(Color.White));
            Console.WriteLine("");

            var table = new Table();
            table.Expand().Border(TableBorder.Rounded).BorderColor(Color.Yellow1);
            table.AddColumn("[yellow]NOTICE: You must accept the and install the certificate that pops up on first use. This allows us to securely capture the data required. No data is saved or re-transmitted.[/]");
            AnsiConsole.Write(table);
            Console.WriteLine("");

            AnsiConsole.MarkupLine(" * You must login using a Chroumium based browser. Firefox will not work.");
            AnsiConsole.MarkupLine(" * Any installed VPN's must be disabled before continuing.");
            Console.WriteLine("");
            Console.WriteLine("");

            AnsiConsole.MarkupLine("Press any key to continue");
            Console.ReadKey(true);

            AnsiConsole.MarkupLine("Starting proxy");
            con.OnUpdateStatus += Program.Controller_OnUpdateStatus;
            con.StartProxy();
            Console.WriteLine("");
            AnsiConsole.MarkupLine("Please login to Onlyfans from your web browser [red](Esc to exit)[/]");

            do {

            } while (Console.ReadKey(true).Key != ConsoleKey.Escape);

            EndProgram();
        }
    
        public class Options
        {
            [Option('p', "port", Required = false, HelpText = "Set Proxy port to use (8000 by default)")]
            public int Port { get; set; }

            [Option('f', "force", Required = false, HelpText = "Forcefully closes the proxy")]
            public bool ForceClose { get; set; }
        }

        static void Controller_OnUpdateStatus(object sender, OFData data)
        {

            con.Stop();

            Console.WriteLine("");
            Console.WriteLine("");
            AnsiConsole.MarkupLine("[yellow]Your OF session details:[/]");
            Console.WriteLine("");

            var table = new Table();
            table.Border(TableBorder.None);
            table.HideHeaders();
            table.AddColumn("Name");
            table.AddColumn("Value");

            table.AddRow("[green]UserId[/]", data.userId);
            table.AddRow("[green]Sess[/]", data.sess);
            table.AddRow("[green]XBC[/]", data.xbc);
            table.AddRow("[green]UserAgent[/]", data.userAgent);
            AnsiConsole.Write(table);

            proxyFinished = true;
            EndProgram();

        }

        static void EndProgram()
        {
            con.Stop();
            Console.WriteLine("");
            AnsiConsole.MarkupLine("[blue]Program terminated[/]");

            Environment.Exit(0);
        }


    }
}