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
        private static Listener listener;

        public static async Task Main()
        {
            // Configurar el evento de cierre del proceso
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

            ConsoleLogger logger = new ConsoleLogger(8900, Listener.clients);
            listener = new Listener(8900, logger); // Listener de pruebas en el puerto 8901

            logger.Start(); // Inicia la actualización de la cabecera
            await listener.StartListening(); // Inicia el Listener
        }

        static void OnProcessExit(object sender, EventArgs e)
        {
            // Lógica para detener el listener de manera segura
            listener?.StopListener();
            Console.WriteLine("El listener ha sido detenido al salir del proceso.");
        }
    }

}