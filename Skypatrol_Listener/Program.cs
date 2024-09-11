using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Skypatrol_Listener
{
        public class Program
    {
        public static async Task Main(string[] args)
        {
            Listener listener = new Listener(8900); // Puerto configurado
            await listener.StartListening();
        }
    }
}