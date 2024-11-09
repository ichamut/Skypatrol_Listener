using Skypatrol_Listener.Skypatrol_Listener;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Skypatrol_Listener
{
    public class ConnectionPool
    {
        //private readonly ConcurrentBag<SqlConnection> connections = new ConcurrentBag<SqlConnection>();
        private readonly string connectionString;

        public ConnectionPool()
        {
            // Obtener la cadena de conexión desde la variable de entorno
            connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("La cadena de conexión no se ha configurado en la variable de entorno.");
            }
        }
        public async Task<SqlConnection> GetConnection(ConsoleLogger logger)
        {
            try
            {
                // Crear y abrir una nueva conexión cada vez que se solicita
                var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException ex)
            {
                // Log del error de conexión
                logger.LogEvent($"Error al obtener la conexión de la base de datos: {ex.Message}");
                throw new InvalidOperationException("No se pudo establecer una conexión con la base de datos.", ex);
            }
            catch (Exception ex)
            {
                // Log de cualquier otro tipo de error inesperado
                logger.LogEvent($"Error inesperado al obtener la conexión: {ex.Message}");
                throw; // Propaga la excepción para que el llamador la maneje
            }
        }
        public void ReturnConnection(SqlConnection connection)
        {
            if (connection != null)
            {
                try
                {
                    connection.Close();
                    connection.Dispose();
                }
                catch (Exception ex)
                {
                    // Log de cualquier error al cerrar la conexión
                    Console.WriteLine($"Error al cerrar la conexión: {ex.Message}");
                }
            }
        }
    }
}
