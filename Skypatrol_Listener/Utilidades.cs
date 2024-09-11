using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.IO;
using Skypatrol_Listener;

namespace Skypatrol_Listener
{
    public static class Utilidades
    {
        public static string AsignaVariables2(byte[] vector, int indexDesde, int largo, int salida)
        {
            StringBuilder result = new StringBuilder();
            for (int i = indexDesde; i < indexDesde + largo; i++)
            {
                if (i >= vector.Length)
                {
                    // En VB6, se omitiría el procesamiento si se pasa del límite del vector
                    continue;
                }

                string varTemp = string.Empty;
                switch (salida)
                {
                    case 1:
                        varTemp = Convert.ToChar(vector[i]).ToString();
                        break;
                    case 2:
                        varTemp = DecToBin2(vector[i]);
                        break;
                    case 3:
                        varTemp = vector[i].ToString("D2");
                        break;
                    case 4:
                        varTemp = vector[i].ToString("X2");
                        break;
                    default:
                        break;
                }
                result.Append(varTemp);
            }
            return result.ToString();
        }
        public static string DecToBin2(long deciValue, int noOfBits = 8)
        {
            while (deciValue > (Math.Pow(2, noOfBits) - 1))
                noOfBits += 8;

            StringBuilder binStr = new StringBuilder();
            for (int i = 0; i < noOfBits; i++)
            {
                binStr.Insert(0, (deciValue & (1 << i)) != 0 ? '1' : '0');
            }
            return binStr.ToString();
        }
        public static string FormatFechaHoraGPS(string fechaHoraGPS)
        {
            return "20" + fechaHoraGPS.Substring(4, 2) + fechaHoraGPS.Substring(2, 2) + fechaHoraGPS.Substring(0, 2) + " " + fechaHoraGPS.Substring(6, 2) + ":" + fechaHoraGPS.Substring(8, 2) + ":" + fechaHoraGPS.Substring(10, 2);
        }
        public static string FormatFechaHoraAVL(string fechaHoraAVL)
        {
            return "20" + fechaHoraAVL.Substring(0, 6) + " " + fechaHoraAVL.Substring(6, 2) + ":" + fechaHoraAVL.Substring(8, 2) + ":" + fechaHoraAVL.Substring(10, 2);
        }
        public static string ByteArrayToHex(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                hex.AppendFormat("{0:x2} ", b);
            return hex.ToString();
        }
        public static string ConvertToLatLong(string value)
        {
            double result = Convert.ToInt64(value, 16);

            if (result > 2147483647)
            {
                result = 4294967295 - result;
                result = -1 * (Math.Floor(result / 1000000) + (result % 1000000) / 600000.0);
            }
            else
            {
                result = Math.Floor(result / 1000000) + (result % 1000000) / 600000.0;
            }

            return result.ToString();
        }
        public static object ValidarFecha(string fechaHora)
        {
            return int.Parse(fechaHora.Substring(0, 4)) > DateTime.Now.Year - 2 ? fechaHora : (object)DBNull.Value;
        }
        public static async Task MandarComando(int clientId, string comando, ConcurrentDictionary<int, TcpClient> clients, Listener listener)
        {
            try
            {
                // Crear un array de bytes para el mensaje completo
                byte[] tramaEnviar = new byte[comando.Length + 7];
                // Configurar los primeros 7 bytes del encabezado API
                tramaEnviar[0] = 0; // Byte 0: Longitud alta del mensaje
                tramaEnviar[1] = (byte)(comando.Length + 7); // Byte 1: Longitud baja del mensaje
                tramaEnviar[2] = 0; // Byte 2: API Number alto
                tramaEnviar[3] = 10; // Byte 3: API Number bajo
                tramaEnviar[4] = 4; // Byte 4: Tipo de comando
                tramaEnviar[5] = 0; // Byte 5: Tipo de mensaje (no necesita ACK)
                tramaEnviar[6] = 0; // Byte 6: Tamaño del encabezado opcional
                                    // Convertir el comando en bytes usando ASCII
                byte[] comandoBytes = Encoding.ASCII.GetBytes(comando);
                // Copiar los bytes del comando al array de tramaEnviar
                Array.Copy(comandoBytes, 0, tramaEnviar, 7, comandoBytes.Length);

                if (clients.TryGetValue(clientId, out TcpClient client))
                {
                    if (client.Connected)
                    {
                        NetworkStream stream = client.GetStream();
                        await stream.WriteAsync(tramaEnviar, 0, tramaEnviar.Length);

                        Console.WriteLine($"Comando enviado al cliente {clientId}: {comando}");
                    }
                    else
                    {
                        Console.WriteLine($"Cliente {clientId} ya no está conectado.");
                        listener.DesconectarCliente(clientId);
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error enviando comando al cliente {clientId}: {ex.Message}");
                listener.DesconectarCliente(clientId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enviando comando al cliente {clientId}: {ex.Message}");
                listener.DesconectarCliente(clientId);
            }
        }
    }
}