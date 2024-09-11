using Skypatrol_Listener.Skypatrol_Listener;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Skypatrol_Listener
{
    namespace Skypatrol_Listener
    {
        public static class Logger
        {
            private static readonly object _lock = new object(); // Lock para concurrencia
            private static readonly string errorLogFilePath = "C:\\Users\\Nacho\\Desktop\\errores.log"; // Ruta al archivo de log de errores
            private static readonly string eventLogFilePath = "C:\\Users\\Nacho\\Desktop\\eventos.log"; // Ruta al archivo de log de eventos

            // Método asíncrono para escribir errores en el archivo de log de errores
            public static async Task LogErrorAsync(string errorMessage)
            {
                await WriteLogAsync(errorLogFilePath, errorMessage);
            }

            // Método asíncrono para escribir eventos en el archivo de log de eventos
            public static async Task LogEventAsync(string eventMessage)
            {
                await WriteLogAsync(eventLogFilePath, eventMessage);
            }

            // Método genérico para escribir en cualquier archivo de log
            private static async Task WriteLogAsync(string logFilePath, string message)
            {
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        try
                        {
                            using (StreamWriter sw = new StreamWriter(logFilePath, true))
                            {
                                sw.WriteLine($"{DateTime.Now}: {message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error al escribir en el archivo de log {logFilePath}: {ex.Message}");
                        }
                    }
                });
            }
        }
    }


}

//así se llamaría a Logger desde cualquier parte:

//string mensajeError = $"Error inesperado: {ex.Message}. Cliente {clientId} desconectado.";

// Llamada síncrona
//Logger.LogErrorToFile(mensajeError);

// Llamada asíncrona si el método es asíncrono
// await Logger.LogErrorToFileAsync(mensajeError);

//Console.WriteLine(mensajeError);
