﻿using Skypatrol_Listener.Skypatrol_Listener;
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
        private readonly ConcurrentBag<SqlConnection> connections = new ConcurrentBag<SqlConnection>();
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
                if (connections.TryTake(out var connection))
                {
                    if (connection.State == System.Data.ConnectionState.Open)
                        return connection;

                    await connection.OpenAsync();  // Intenta abrir la conexión si estaba cerrada
                    return connection;
                }
                else
                {
                    connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();  // Abre una nueva conexión
                    return connection;
                }
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
    }
}
