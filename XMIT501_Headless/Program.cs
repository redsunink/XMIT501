using System;
using System.Threading;

namespace XMIT501_Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing XMIT501 Headless Server...");
            var server = new RadioServer(5000);
            Console.WriteLine("Press Ctrl+C to shutdown.");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}