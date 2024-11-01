using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System;
using Skypatrol_Listener;

public class ConsoleLogger
{
    private readonly int port;
    private readonly Stopwatch stopwatch;
    private readonly ConcurrentDictionary<int, TcpClient> clients;
    private readonly object consoleLock = new object();
    // Definir el huso horario de Argentina
    private readonly TimeZoneInfo argentinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
    private readonly DateTime fechaHoraInicio = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time"));
    private int logRow = 8; // Fila a partir de la cual se empezarán a escribir logs
    private long totalTramasConsola = 0;

    public ConsoleLogger(int port, ConcurrentDictionary<int, TcpClient> clients)
    {
        this.port = port;
        this.clients = clients;
        this.stopwatch = Stopwatch.StartNew();
    }
    public double GetTramasPerMinute(long totalTramas)
    {
        double minutesElapsed = stopwatch.Elapsed.TotalMinutes;
        totalTramasConsola = totalTramas;
        return minutesElapsed > 0 ? (double)totalTramas / minutesElapsed : 0;
    }

    public void Start()
    {
        Task.Run(UpdateHeader); // Inicia la tarea para actualizar la cabecera
    }

    public void LogEvent(string message)
    {
        lock (consoleLock)
        {
            DateTime fechaHoraArgentina = TimeZoneInfo.ConvertTime(DateTime.Now, argentinaTimeZone);
            message = message.EndsWith("\n") ? message : message + "\n";
            message = $"[{fechaHoraArgentina}]: {message}.";

            // Ajustar el ancho máximo de cada línea al tamaño del buffer de la consola
            int maxLineWidth = Console.BufferWidth - 1;


            // Dividir el mensaje en varias líneas si excede el ancho de la consola
            for (int i = 0; i < message.Length; i += maxLineWidth)
            {
                string line = message.Substring(i, Math.Min(maxLineWidth, message.Length - i));
                // Mueve el cursor a la fila de logs (logRow) sin sobreescribir la cabecera
                Console.SetCursorPosition(0, logRow++);
                Console.WriteLine(line);
            }

            // Limpiar si logRow llega al final de la pantalla
            if (logRow >= Console.WindowHeight - 2)
            {
                //Console.Clear();
                //logRow = 8; // Reinicia la fila para nuevos logs
            }
        }
    }

    private async Task UpdateHeader()
    {
        while (true)
        {
            lock (consoleLock)
            {
                DateTime fechaHoraArgentina = TimeZoneInfo.ConvertTime(DateTime.Now, argentinaTimeZone);
                // Mueve el cursor a la parte superior de la consola y sobrescribe la cabecera
                Console.SetCursorPosition(0, 0);
                // Cabecera fija
                Console.WriteLine($"Hora (Argentina): {fechaHoraArgentina}.");
                Console.WriteLine($"Hora de inicio (Argentina): {fechaHoraInicio}.");
                Console.WriteLine($"Escuchando por puerto: {port}.");
                Console.WriteLine($"Se han cargado 1571 rastreadores de Skypatrol habilitados en memoria.");
                Console.WriteLine($"Conexiones activas: {clients.Count}.");
                // Verificamos si listener es null antes de acceder a totalTramas
                Console.WriteLine($"Ratio de tramas por minuto: {GetTramasPerMinute(totalTramasConsola)}.");
                Console.WriteLine($"Total de tramas procesadas: {totalTramasConsola}.");
                Console.WriteLine("---------------------------------------------");
                // Limpiar el área de logs anteriores
                Console.SetCursorPosition(0, logRow);
            }

            // Espera 1 segundo antes de actualizar la cabecera
            await Task.Delay(1000);
        }
    }
}
