using Skypatrol_Listener;
using Skypatrol_Listener.Skypatrol_Listener;
using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

    public class ComandosHandler
{
    private readonly SqlConnection _connection;
    private readonly ConcurrentDictionary<int, TcpClient> _clients;
    private readonly Listener _listener;
    private readonly ConsoleLogger _logger;

    public ComandosHandler(SqlConnection connection, ConcurrentDictionary<int, TcpClient> clients, Listener listener, ConsoleLogger logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task Ejecutar(string codigoComando, string trama, int conexIndex, int port, string imei)
    {
        int retryCount = 0;
        bool success = false;

        while (!success && retryCount < 3)
        {
            try
            {
                using (SqlCommand cmd = new SqlCommand("sp_comandos", _connection))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;

                    // Parámetro @trama
                    SqlParameter param1 = new SqlParameter("@trama", System.Data.SqlDbType.VarChar, 1550);
                    if (string.IsNullOrEmpty(trama))
                    {
                        param1.Value = DBNull.Value;
                    }
                    else
                    {
                        param1.Value = trama.Length < 1550 ? trama : "Demasiados caracteres en trama";
                    }
                    cmd.Parameters.Add(param1);

                    // Parámetro @codigo_comando
                    SqlParameter param2 = new SqlParameter("@codigo_comando", System.Data.SqlDbType.VarChar, 15);
                    if (string.IsNullOrEmpty(codigoComando))
                    {
                        param2.Value = DBNull.Value;
                    }
                    else
                    {
                        param2.Value = codigoComando;
                    }
                    cmd.Parameters.Add(param2);

                    // Parámetro @index
                    cmd.Parameters.AddWithValue("@index", conexIndex);

                    // Parámetro @puerto
                    cmd.Parameters.AddWithValue("@puerto", port.ToString());

                    // Parámetro @imei
                    cmd.Parameters.AddWithValue("@imei", imei);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            await Utilidades.MandarComando(reader.GetInt32(1), reader.GetString(0), _clients, _listener, _logger);
                        }
                    }
                }
                success = true; // Transacción exitosa
            }
            catch (SqlException ex) when (ex.Number == 1205) // Código de error para deadlock
            {
                retryCount++;
                _logger.LogEvent($"Error (Deadlock) SQL en ComandosHandler: {ex.Message}.");
                // Aquí podrías agregar lógica para manejar el error, como registrar en una tabla de errores.
                await Task.Delay(500); // Espera antes de reintentar
            }
            catch (SqlException ex)
            {
                _logger.LogEvent($"Error SQL en ComandosHandler (no Deadlock): {ex.Message}");
                // Puedes registrar el error o manejarlo de forma específica sin reintento
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogEvent($"Operación inválida en ComandosHandler: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogEvent($"Error inesperado en ComandosHandler: {ex.Message}");
                break;
            }
        }
    }
}