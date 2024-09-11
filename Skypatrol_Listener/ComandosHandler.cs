using Skypatrol_Listener;
using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

    public class ComandosHandler
{
    private SqlConnection _connection;
    private ConcurrentDictionary<int, TcpClient> _clients;
    private Listener _listener;

    public ComandosHandler(SqlConnection connection, ConcurrentDictionary<int, TcpClient> clients, Listener listener)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
    }
    public async Task Ejecutar(string codigoComando, string trama, int conexIndex, int port, string imei)
    {
        try
        {
            using (SqlCommand cmd = new SqlCommand("comandos_a_enviar", _connection))
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
                        await Utilidades.MandarComando(reader.GetInt32(1), reader.GetString(0), _clients, _listener);
                    }
                }
            }
        }
        catch (SqlException ex)
        {
            Console.WriteLine($"Error SQL en ComandosHandler: {ex.Message}");
            // Aquí podrías agregar lógica para manejar el error, como registrar en una tabla de errores.
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Operación inválida en ComandosHandler: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error inesperado en ComandosHandler: {ex.Message}");
        }
    }
}