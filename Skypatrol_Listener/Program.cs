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
        public static async Task Main()
        {
            
            ConsoleLogger logger = new ConsoleLogger(8900, Listener.clients);
            Listener listener = new Listener(8900, logger); // Crea el Listener
            logger.Start(); // Inicia la actualización de la cabecera

            await listener.StartListening();
        }
    }
}