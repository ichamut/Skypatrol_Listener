using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Data;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using Skypatrol_Listener.Skypatrol_Listener;

namespace Skypatrol_Listener
{
    public class Listener
    {
        private readonly int port;
        private readonly TcpListener tcpListener;
        private readonly HashSet<string> imeisHabilitados;
        public static readonly ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
        private static readonly ConnectionPool connectionPool = new ConnectionPool();
        private DateTime lastCommandCheckTime = DateTime.MinValue; // Marca de tiempo para la última verificación de comando
        private readonly ConsoleLogger _logger;
        public long totalTramas = 0;
        private bool comandosActivos;

        public Listener(int port, ConsoleLogger logger)
        {
            this.port = port;
            this._logger = logger;  // Logger inyectado
            tcpListener = new TcpListener(IPAddress.Any, port);
            imeisHabilitados = new HashSet<string>();
        }
        public async Task StartListening()
        {
            tcpListener.Start();
            await CargarImeisHabilitados();
            comandosActivos = await EstadoFuncionEnvioComando(port); //pregunta si la funcion de envío de comandos está activa
            _logger.LogEvent($"Envío de comandos: {comandosActivos}.");
            //Console.WriteLine($"Listening on port {port}...");

            // Agregar cliente al diccionario concurrente
            while (true)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    int clientId = Math.Abs(client.Client.RemoteEndPoint.GetHashCode());

                    // Intentar agregar el cliente con el clientId inicial
                    while (!clients.TryAdd(clientId, client))
                    {
                        //_logger.LogEvent($"Cliente con clientId {clientId} ya existe. Generando nuevo ID...");

                        // Generar un nuevo clientId alternativo y asegurar que sea positivo
                        clientId = Math.Abs(clientId + 1);
                    }

                    //_logger.LogEvent($"Cliente {clientId} añadido exitosamente.");
                    _ = Task.Run(() => HandleClient(clientId)); // Maneja el cliente en una tarea independiente
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogEvent("Listener detenido.");
                    break; // Salir del bucle si el Listener fue detenido
                }
                catch (Exception ex)
                {
                    _logger.LogEvent($"Error inesperado en StartListening: {ex.Message}");
                    break;
                }
            }
        }
        // Método para detener el listener antes de reiniciar
        public void StopListener()
        {
            try
            {
                _logger.LogEvent("Deteniendo el listener...");

                // Cerrar todos los clientes conectados
                foreach (var clientId in clients.Keys)
                {
                    DesconectarCliente(clientId);
                }

                // Detener el listener para liberar el puerto
                tcpListener.Stop();
                _logger.LogEvent("Listener detenido y recursos liberados.");
            }
            catch (Exception ex)
            {
                _logger.LogEvent($"Error al detener el listener: {ex.Message}");
            }
        }
        private async Task<bool> EstadoFuncionEnvioComando(int port)
        {
            bool comandosActivos = false; // Inicializa en `false` por defecto

            using (var connection = await connectionPool.GetConnection(_logger))
            {
                using (SqlCommand cmd = new SqlCommand("select ComandosActivos from puertos_sistema where Puerto=@port", connection))
                {
                    cmd.Parameters.AddWithValue("@port", port); // Parámetro de consulta SQL seguro

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync()) // Lee solo un registro
                        {
                            comandosActivos = reader.GetBoolean(0); // Asigna el valor bit directamente a la variable booleana
                        }
                    }
                }
            }

            return comandosActivos; // Retorna el valor booleano de la columna `ComandosActivos`
        }
        private async Task CargarImeisHabilitados()
        {

            using (var connection = await connectionPool.GetConnection(_logger))
            {
                using (SqlCommand cmd = new SqlCommand("select imei from avl where id_avl_modelo in (select id_avl_modelo from avl_modelo where fabricante = 'Skypatrol')", connection))
                {
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string imei = reader.GetString(0);
                            imeisHabilitados.Add(imei);
                        }
                    }
                }
            }

            //Console.WriteLine($"Se han cargado {imeisHabilitados.Count} IMEIs habilitados en memoria.");
        }
        private bool VerificarImei(string imei)
        {
            return imeisHabilitados.Contains(imei);
        }
        private async Task GuardarTramaEnBD(long? imei, byte[] receivedData)
        {
            // Definir el huso horario de Argentina
            TimeZoneInfo argentinaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");

            // Obtener la fecha y hora actual en Argentina
            DateTime fechaHoraArgentina = TimeZoneInfo.ConvertTime(DateTime.Now, argentinaTimeZone);

            using (var connection = await connectionPool.GetConnection(_logger))
            {
                using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO tramas_skypatrol (fecha_hora_sistema, imei, Trama) VALUES (@FechaHora, @imei, @Trama)", connection))
                {
                    cmd.Parameters.AddWithValue("@FechaHora", fechaHoraArgentina);
                    cmd.Parameters.AddWithValue("@imei", imei.HasValue ? imei : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Trama", receivedData);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        private async Task HandleClient(int clientId)
        {
            if (!clients.TryGetValue(clientId, out var client))
            {
                _logger.LogEvent($"Cliente {clientId} no encontrado al intentar manejar la conexión.");
                return;
            }

            NetworkStream networkStream = null;

            try
            {
                networkStream = client.GetStream();
                byte[] buffer = new byte[4096];
                int bytesRead;

                //_logger.LogEvent($"Cliente {clientId} conectado. Conexiones activas: {clients.Count}");

                DateTime lastActivityTime = DateTime.UtcNow;
                TimeSpan timeoutPeriod = TimeSpan.FromMinutes(20);

                while (true)
                {
                    try
                    {
                        if (!networkStream.DataAvailable) // Verificar si hay datos disponibles
                        {
                            // Verificar tiempo de espera por inactividad
                            if ((DateTime.UtcNow - lastActivityTime) > timeoutPeriod)
                            {
                                DesconectarCliente(clientId);
                                //_logger.LogEvent($"Cliente {clientId} desconectado por inactividad. Conexiones activas: {clients.Count}");
                                break; // Salir del bucle si el cliente ha estado inactivo demasiado tiempo
                            }

                            await Task.Delay(1000); // Espera un segundo antes de verificar de nuevo
                            continue; // Continúa con la próxima iteración
                        }

                        bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            if (!clients.ContainsKey(clientId))
                                break; // Cliente ya fue desconectado, salir del bucle

                            lastActivityTime = DateTime.UtcNow; // Actualiza el tiempo de la última actividad
                            byte[] receivedData = new byte[bytesRead];
                            Array.Copy(buffer, receivedData, bytesRead);

                            // Proceso la trama y guardo
                            await ProcessData(receivedData, clientId);
                        }
                        else
                        {
                            //_logger.LogEvent($"Cliente {clientId} desconectado inesperadamente. Conexiones activas: {clients.Count - 1}");
                            break;
                        }
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException socketEx)
                    {
                        //_logger.LogEvent($"Error de Socket: {socketEx.Message}. Cliente {clientId} desconectado inesperadamente.");
                        break;
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogEvent($"Intento de acceso a un objeto desechado para el cliente {clientId}. Ignorando excepción.");
            }
            catch (Exception ex)
            {
                _logger.LogEvent($"Error inesperado: {ex.Message}. Cliente {clientId} desconectado.");
                _logger.LogEvent($"Detalles del error inesperado: {ex.StackTrace}");
            }
            finally
            {
                clients.TryRemove(clientId, out var clientRemoved);
                networkStream?.Close();
                clientRemoved?.Close();

                //_logger.LogEvent($"Cliente {clientId} desconectado. Conexiones activas: {clients.Count}");
            }
        }

        public void DesconectarCliente(int clientId)
        {
            try
            {
                if (clients.TryGetValue(clientId, out TcpClient client))
                {
                    if (client.Connected) // Verifica si el cliente está conectado antes de cerrar
                    {
                        try
                        {
                            // Forzar el cierre TCP enviando una señal de cierre
                            client.Client.Shutdown(SocketShutdown.Both);
                            client.Close(); // Intenta cerrar la conexión
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            //_logger.LogEvent($"El cliente {clientId} cerró la conexión de forma abrupta.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogEvent($"Error al cerrar la conexión para el cliente {clientId}: {ex.Message}");
                        }
                    }
                    clients.TryRemove(clientId, out _); // Remueve el cliente de la lista de clientes activos
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogEvent($"Intento de acceso a un objeto desechado al desconectar el cliente {clientId}. Ignorando excepción.");
            }
            catch (Exception ex)
            {
                _logger.LogEvent($"Error inesperado al intentar desconectar el cliente {clientId}: {ex.Message}");
            }
        }
        private async Task ProcessData(byte[] data, int clientId)
        {
            using (var connection = await connectionPool.GetConnection(_logger))
            {
                try
                {
                    int inicio = 0;
                    string imei = Utilidades.AsignaVariables2(data, 15, 22, 1).Trim();
                    string imei_trama_respuesta = Utilidades.AsignaVariables2(data, 15, 17, 1).Trim();
                    string imei_correcto = VerificarImei(imei) ? imei : imei_trama_respuesta;
                    string tipoComando = Utilidades.AsignaVariables2(data, inicio + 4, 1, 3);
                    
                    // Verificar si el IMEI está habilitado
                    if (!VerificarImei(imei_correcto) && (tipoComando != "05"))
                    {
                        _logger.LogEvent($"IMEI no habilitado: {imei}. Desconectando cliente {clientId}. IMEI de respuesta: {imei_trama_respuesta}.");
                        // Desconectar al cliente no autorizado
                        DesconectarCliente(clientId);
                        return; // Ignorar procesamiento adicional
                    }
                    // Mando la trama a la BD tal cual llega
                    // Variable nullable para el IMEI
                    long? imeiBigInt = long.TryParse(imei_correcto, out long result) ? (long?)result : null;

                    await GuardarTramaEnBD(imeiBigInt, data);
                    while (inicio < data.Length)
                    {
                        string largoTrama = Utilidades.AsignaVariables2(data, inicio, 2, 3);
                        string numApi = Utilidades.AsignaVariables2(data, inicio + 2, 2, 3);
                        // Incrementa el contador de tramas
                        totalTramas++;
                        _logger.GetTramasPerMinute(totalTramas);
                        switch (tipoComando)
                        {
                            case "02":
                                await LogError(connection, "Mensaje GPRS de skypatrol", clientId, Utilidades.ByteArrayToHex(data));
                                break;

                            case "08":
                                string tipoMensajeAck = Utilidades.AsignaVariables2(data, inicio + 5, 1, 4);
                                switch (tipoMensajeAck[0])
                                {
                                    case '0': //Keep-alive message y trama de presentación
                                        HandleKeepAliveMessage(data, connection, clientId, inicio);
                                        break;

                                    case '1': //Tramas siguientes
                                        await HandlePositionMessage(data, connection, clientId, inicio, largoTrama);
                                        break;

                                    default:
                                        await LogError(connection, "Tipo de mensaje ACK desconocido", clientId, Utilidades.ByteArrayToHex(data));
                                        break;
                                }
                                break;

                            case "05":
                                await HandleCommandResponse(data, connection, clientId, imei_correcto, largoTrama);
                                break;

                            default:
                                await LogError(connection, "Trama que no es de skypatrol", clientId, Utilidades.ByteArrayToHex(data));
                                break;
                        }

                        inicio += Convert.ToInt32(largoTrama);
                    }
                }
                catch (Exception ex)
                {
                    await LogError(connection, ex.Message, clientId, Utilidades.ByteArrayToHex(data));
                }
            }
        }
        private void HandleKeepAliveMessage(byte[] data, SqlConnection connection, int clientId, int inicio)
        {
            string imei = Utilidades.AsignaVariables2(data, inicio + 11, 22, 1).Trim();
            // Lógica para actualizar la conexión en la base de datos
            using (SqlCommand cmd = new SqlCommand("sp_update_index_actual", connection))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@Index", clientId);
                cmd.Parameters.AddWithValue("@Puerto", port);
                cmd.Parameters.AddWithValue("@Imei", imei);
                cmd.ExecuteNonQuery();
            }
        }
        private async Task HandlePositionMessage(byte[] data, SqlConnection connection, int clientId, int inicio, string largoTrama)
        {
            ComandosHandler config = new ComandosHandler(connection, clients, this, _logger);//Para consultar comandos pendientes y gestionar respuestas de ubicación
            int correccion = 0;
            int mascara = Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 7, 4, 4), 16);

            if (mascara == 427556735)
                correccion = 4;
            string codigoEvento = Utilidades.AsignaVariables2(data, inicio + 7 + correccion, 4, 3).TrimStart('0');
            string imei = Utilidades.AsignaVariables2(data, inicio + 11 + correccion, 22, 1).Trim();
            string statusHard = Utilidades.AsignaVariables2(data, inicio + 33 + correccion, 2, 2);
            string AD1 = (Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + correccion + 35, 2, 4), 16) / 1000.0).ToString();
            string AD2 = (Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + correccion + 37, 2, 4), 16) / 1000.0).ToString();

            string fechaHoraGPS = Utilidades.AsignaVariables2(data, inicio + 39 + correccion, 3, 3);
            fechaHoraGPS = Utilidades.FormatFechaHoraGPS(fechaHoraGPS + Utilidades.AsignaVariables2(data, inicio + 55 + correccion, 3, 3));
            int gpsStatus = Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 42 + correccion, 1, 3));
            string latitud = Utilidades.ConvertToLatLong(Utilidades.AsignaVariables2(data, inicio + 43 + correccion, 4, 4));
            string longitud = Utilidades.ConvertToLatLong(Utilidades.AsignaVariables2(data, inicio + 47 + correccion, 4, 4));
            string velocidad = (Convert.ToDouble(Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 51 + correccion, 2, 4), 16)) / 10 * 1.852).ToString();

            string direccion = (Convert.ToDouble(Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 53 + correccion, 2, 4), 16)) / 10).ToString();
            int voltsBatInt = (Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 62 + correccion, 2, 3),16)) / 100;
            int hdop = gpsStatus == 1 ? 1 : 0;
            int cantidadSatelites = hdop == 1 ? Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 61 + correccion, 1, 3)) : 0;
            int voltajeAvlAgotado = Convert.ToDouble(voltsBatInt) < 3.6 ? 1 : 0;
            string altitud = Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 58 + correccion, 3, 4), 16).ToString();

            if (mascara == 427556735 || mascara == 34)
                correccion += 8;
                string Kilometros = Utilidades.AsignaVariables2(data, inicio + correccion + 68, 4, 3);

            string fechaHoraAVL = Utilidades.FormatFechaHoraAVL(Utilidades.AsignaVariables2(data, inicio + 64 + correccion, 6, 3));
            string voltsBatExt = (Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 70 + correccion, 2, 4), 16) / 1000.0).ToString();
            int numTrama = Convert.ToInt32(Utilidades.AsignaVariables2(data, inicio + 72 + correccion, 2, 4), 16);
            string comandoUbicacion = "AT$TTLOGRD=4,1,0";
            string comandoCodigo = "AT$TTLOGRD";           
            string ipAvl = GetRemoteIPAddress(clientId);
            string EventoOriginal = codigoEvento;

            if (fechaHoraAVL.Substring(2, 2) == "11" ||
                fechaHoraAVL.Substring(2, 2) == "04" ||
                fechaHoraAVL.Substring(2, 2) == "00")
            {
                // Registrar el error
                await LogError( connection, "Mando comando para corregir la Fecha y hora (Skypatrol-Trama 2011 o 2003) si EventoOriginal != 34: " + EventoOriginal, clientId, "IMEI: " + imei);
                // Verificar condición adicional antes de enviar el comando
                if (EventoOriginal != "34")
                {
                    // Sumar 7 horas a la fecha y hora actual
                    DateTime t = DateTime.Now.AddHours(7);
                    // Construir el comando
                    string comando = $"AT$TTRTCTI=,{t:yy},{t:MM},{t:dd},{t:HH},{t:mm},{t:ss}";
                    await Utilidades.MandarComando(clientId, comando, clients, this, _logger);
                }

                return; // Equivalente al GoTo Fin_Funcion
            }

            switch (codigoEvento)
            {
                case "1":
                    codigoEvento = "102"; // Pánico
                    break;
                case "19":
                    codigoEvento = "103"; // Velocidad máxima excedida
                    break;
                case "20":
                    codigoEvento = "104"; // Recupera velocidad permitida
                    break;
                case "28":
                    codigoEvento = "119"; // Desconexión/corte de antena GPS
                    break;
                case "26":
                    codigoEvento = "114"; // Sleep mode
                    break;
                case "27":
                    codigoEvento = "115"; // Wake up
                    break;
                case "29":
                    codigoEvento = "120"; // Reinicio del AVL
                    break;
                case "34":
                    codigoEvento = "respuesta_ubicacion"; // Debe llamar a store de actualizar datos actuales
                    break;
                case "32":
                    codigoEvento = "122"; // Reporte por giro
                    break;
                case "35":
                    codigoEvento = "121"; // Reporte por intervalo de tiempo
                    break;
                case "131":
                    codigoEvento = "131"; // Reporte por intervalo de tiempo
                    break;
                default:
                    codigoEvento = "";
                    break;
            }


            if (codigoEvento != "respuesta_ubicacion")
            {
                // Ejecutar el procedimiento almacenado principal
                using (SqlCommand cmd = new SqlCommand("sp_principal", connection))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@latitud_trama", latitud.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@longitud_trama", longitud.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@hora_satelite", Utilidades.ValidarFecha(fechaHoraGPS));
                    cmd.Parameters.AddWithValue("@evento_sin_status", codigoEvento);
                    cmd.Parameters.AddWithValue("@imei_hard", imei);
                    cmd.Parameters.AddWithValue("@status_Hard", statusHard);
                    cmd.Parameters.AddWithValue("@voltaje_avl", voltsBatInt);
                    cmd.Parameters.AddWithValue("@voltaje_vehiculo", voltsBatExt.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@velocidad", velocidad.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@direcc", direccion.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@tramas_recibidas", numTrama);
                    cmd.Parameters.AddWithValue("@hdop", hdop);
                    cmd.Parameters.AddWithValue("@kilometros", Kilometros.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@ip", ipAvl);
                    cmd.Parameters.AddWithValue("@conex", clientId);
                    cmd.Parameters.AddWithValue("@puerto", port);
                    cmd.Parameters.AddWithValue("@hora_AVL_de_trama", Utilidades.ValidarFecha(fechaHoraAVL));
                    cmd.Parameters.AddWithValue("@voltaje1", AD1.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@voltaje2", AD2.Replace(',', '.'));
                    cmd.Parameters.AddWithValue("@voltaje_avl_agotado", voltajeAvlAgotado);
                    cmd.Parameters.AddWithValue("@evento_original", EventoOriginal);
                    cmd.Parameters.AddWithValue("@comando_ubicacion", comandoUbicacion);
                    cmd.Parameters.AddWithValue("@comando_codigo", comandoCodigo);
                    cmd.Parameters.AddWithValue("@gsm_signal", cantidadSatelites);
                    cmd.Parameters.AddWithValue("@altura", altitud.Replace(',', '.'));
                    await cmd.ExecuteNonQueryAsync();
                }
                // Aquí consulto e intento enviar los comandos pendientes si la funcion está activa en la tabla puertos_sistema, columna ComandosActivos
                if ((DateTime.UtcNow - lastCommandCheckTime).TotalSeconds >= 10) // Verificar si han pasado 10 segundos
                {
                    if (comandosActivos)
                    {
                        await config.Ejecutar("", "", clientId, port, imei); // Consultar comandos pendientes
                        lastCommandCheckTime = DateTime.UtcNow; // Actualizar el tiempo de la última verificación
                    }
                }
            }
            else
            {
                using (SqlCommand cmdUpdateUbicacion = new SqlCommand("sp_update_ubicacion_actual", connection))
                {
                    cmdUpdateUbicacion.CommandType = CommandType.StoredProcedure;
                    cmdUpdateUbicacion.Parameters.AddWithValue("@latitud", latitud.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@longitud", longitud.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@hora_AVL", Utilidades.ValidarFecha(fechaHoraGPS));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@velocidad", velocidad.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@hdop", hdop);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@index", clientId);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@puerto", port);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@voltaje_avl", voltsBatInt);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@voltaje_vehiculo", voltsBatExt.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@gsm_signal", cantidadSatelites);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@altura", altitud.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@status_hard", statusHard);
                    cmdUpdateUbicacion.Parameters.AddWithValue("@direcc", direccion.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@voltaje_sensor1", AD1.Replace(',', '.'));
                    cmdUpdateUbicacion.Parameters.AddWithValue("@voltaje_sensor2", AD2.Replace(',', '.'));
                    await cmdUpdateUbicacion.ExecuteNonQueryAsync();
                }
                //Actualizo la respuesta del rastreador en tabla comandos_pendientes
                if (comandosActivos)//Aquí no estoy seguro de que deba ir este if ya que supuestamente debe gestionar la respuesta del rastreador, aunque sospecho que también envía comandos
                {
                    await config.Ejecutar(comandoCodigo, Utilidades.AsignaVariables2(data, inicio, Convert.ToInt32(largoTrama), 4), clientId, port, imei);
                }
            }
        }
        private async Task HandleCommandResponse(byte[] data, SqlConnection connection, int clientId, string imei, string largoTrama)
        {
            // Verificar si la conversión es válida
            // Inicializamos result1 y result2 con un valor predeterminado
            int result1 = 0;
            int result2 = 0;
            if (int.TryParse(Utilidades.AsignaVariables2(data, 7, 4, 3), out result1) || int.TryParse(Utilidades.AsignaVariables2(data, 11, 4, 3), out result2))
            {
                if (result1 == 34 || result2 == 34)
                {
                    await HandlePositionMessage(data, connection, clientId, 0, largoTrama);
                    return;
                }
            }
            else
            {
                _logger.LogEvent($"Error: No se pudo convertir parte de la trama a entero en el cliente {clientId}.");
            }


            string respuesta = Utilidades.AsignaVariables2(data, 7, data.Length - 7, 1);
            string[] codigoResp = respuesta.Split(new[] { "\r\n" }, StringSplitOptions.None);

            if (codigoResp.Length > 1)
            {
                string comando;
                string respuestaSinIntro = respuesta; // Para la lógica de respuesta limpia en el caso "Else"

                if (codigoResp[1] != "OK")
                {
                    comando = "AT" + codigoResp[1];
                }
                else
                {
                    comando = codigoResp[1];
                    // Actualiza 'respuestaSinIntro' como en VB6
                    string[] respSinIntro = respuesta.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (respSinIntro.Length > 1)
                    {
                        respuestaSinIntro = respSinIntro[1];  // Obtener la segunda línea
                    }
                }

                // Ejecutar la lógica de comandos
                ComandosHandler config = new ComandosHandler(connection, clients, this, _logger);
                await config.Ejecutar(comando, respuestaSinIntro, clientId, port, imei);
            }

        }
        private string GetRemoteIPAddress(int clientId)
        {
            if (clients.TryGetValue(clientId, out TcpClient client))
            {
                return ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            }
            return "Unknown";
        }
        private async Task LogError(SqlConnection connection, string comentario, int clientId, string trama)
        {
            try
            {
                    using (SqlCommand cmd = new SqlCommand("sp_insert_error_log", connection))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;

                        // Parámetros
                        cmd.Parameters.AddWithValue("@comentario", comentario);
                        cmd.Parameters.AddWithValue("@ip", GetRemoteIPAddress(clientId));
                        cmd.Parameters.AddWithValue("@conex_index", clientId);
                        cmd.Parameters.AddWithValue("@trama", trama);
                        cmd.Parameters.AddWithValue("@puerto", port);

                        await cmd.ExecuteNonQueryAsync();
                    }
            }
            catch (SqlException sqlEx)
            {
                // Manejar excepciones específicas de SQL
                _logger.LogEvent($"SQL Error: {sqlEx.Message}");
            }
            catch (Exception ex)
            {
                // Manejar otras excepciones generales
                _logger.LogEvent($"General Error: {ex.Message}");
            }
        }
    }

}

