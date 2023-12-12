﻿#region Licence

/* The MIT License (MIT)
Copyright © 2014 Francesco Pighi <francesco.pighi@gmail.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.Logging;
using Paramore.Brighter.Sqlite;

namespace Paramore.Brighter.Outbox.Sqlite
{
    /// <summary>
    /// Implements an outbox using Sqlite as a backing store
    /// </summary>
    public class SqliteOutbox : RelationDatabaseOutbox
    {
        private static readonly ILogger s_logger = ApplicationLogging.CreateLogger<SqliteOutbox>();

        private const int SqliteDuplicateKeyError = 1555;
        private const int SqliteUniqueKeyError = 19;
        private readonly IAmARelationalDatabaseConfiguration _configuration;
        private readonly IAmARelationalDbConnectionProvider _connectionProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteOutbox" /> class.
        /// </summary>
        /// <param name="configuration">The configuration to connect to this data store</param>
        /// <param name="connectionProvider">Provides a connection to the Db that allows us to enlist in an ambient transaction</param>
        public SqliteOutbox(IAmARelationalDatabaseConfiguration configuration, IAmARelationalDbConnectionProvider connectionProvider) 
            : base(configuration.OutBoxTableName, new SqliteQueries(), ApplicationLogging.CreateLogger<SqliteOutbox>())
        {
            _configuration = configuration;
            ContinueOnCapturedContext = false;
            _connectionProvider = connectionProvider;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteOutbox" /> class.
        /// </summary>
        /// <param name="configuration">The configuration to connect to this data store</param>
        public SqliteOutbox(IAmARelationalDatabaseConfiguration configuration) 
            : this(configuration, new SqliteConnectionProvider(configuration))
        {
        }

        protected override void WriteToStore(
            IAmABoxTransactionProvider<DbTransaction> transactionProvider,
            Func<DbConnection, DbCommand> commandFunc,
            Action loggingAction
            )
        {
            var connectionProvider = _connectionProvider;
            if (transactionProvider is IAmARelationalDbConnectionProvider transConnectionProvider)
                connectionProvider = transConnectionProvider;

            var connection = connectionProvider.GetConnection();

            if (connection.State != ConnectionState.Open)
                connection.Open();
            using (var command = commandFunc.Invoke(connection))
            {
                try
                {
                    if (transactionProvider != null && transactionProvider.HasOpenTransaction)
                        command.Transaction = transactionProvider.GetTransaction();
                    command.ExecuteNonQuery();
                }
                catch (SqliteException sqlException)
                {
                    if (IsExceptionUnqiueOrDuplicateIssue(sqlException))
                    {
                        loggingAction.Invoke();
                        return;
                    }

                    throw;
                }
                finally
                {
                    if (transactionProvider != null)
                        transactionProvider.Close();
                    else
                        connection.Close();
                }
            }
        }

        protected override async Task WriteToStoreAsync(
            IAmABoxTransactionProvider<DbTransaction> transactionProvider,
            Func<DbConnection, DbCommand> commandFunc,
            Action loggingAction, 
            CancellationToken cancellationToken)
        {
            var connectionProvider = _connectionProvider;
            if (transactionProvider is IAmARelationalDbConnectionProvider transConnectionProvider)
                connectionProvider = transConnectionProvider;

            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

            if (connection.State != ConnectionState.Open)
                connection.Open();
            using (var command = commandFunc.Invoke(connection))
            {
                try
                {
                    if (transactionProvider != null && transactionProvider.HasOpenTransaction)
                        command.Transaction = await transactionProvider.GetTransactionAsync(cancellationToken);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqliteException sqlException)
                {
                    if (IsExceptionUnqiueOrDuplicateIssue(sqlException))
                    {
                        loggingAction.Invoke();
                        return;
                    }

                    throw;
                }
                finally
                {
                    if (transactionProvider != null)
                        transactionProvider.Close();
                    else
#if NETSTANDARD2_0
                        connection.Close();
#else
                        await connection.CloseAsync();
#endif
                }
            }
        }

        protected override T ReadFromStore<T>(
            Func<DbConnection, DbCommand> commandFunc,
            Func<DbDataReader, T> resultFunc
            )
        {
            var connection = _connectionProvider.GetConnection();

            if (connection.State != ConnectionState.Open)
                connection.Open();
            using (var command = commandFunc.Invoke(connection))
            {
                try
                {
                    return resultFunc.Invoke(command.ExecuteReader());
                }
                finally
                {
                    connection.Close();
                }
            }
        }

        protected override async Task<T> ReadFromStoreAsync<T>(
            Func<DbConnection, DbCommand> commandFunc,
            Func<DbDataReader, Task<T>> resultFunc, 
            CancellationToken cancellationToken)
        {
            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);
            using (var command = commandFunc.Invoke(connection))
            {
                try
                {
                    return await resultFunc.Invoke(await command.ExecuteReaderAsync(cancellationToken));
                }
                finally
                {
#if NETSTANDARD2_0
                    connection.Close();
#else
                    await connection.CloseAsync();
#endif
                }
            }
        }

        protected override DbCommand  CreateCommand(
            DbConnection connection, 
            string sqlText, 
            int outBoxTimeout,
            params IDbDataParameter[] parameters)
        {
            var command = connection.CreateCommand();

            command.CommandTimeout = outBoxTimeout < 0 ? 0 : outBoxTimeout;
            command.CommandText = sqlText;
            command.Parameters.AddRange(parameters);

            return command;
        }

        protected override IDbDataParameter[] CreatePagedOutstandingParameters(
            double milliSecondsSinceAdded,
            int pageSize, int pageNumber
            )
        {
            var parameters = new IDbDataParameter[3];
            parameters[0] = CreateSqlParameter("PageNumber", pageNumber);
            parameters[1] = CreateSqlParameter("PageSize", pageSize);
            parameters[2] = CreateSqlParameter("OutstandingSince", milliSecondsSinceAdded);

            return parameters;
        }

        protected override IDbDataParameter CreateSqlParameter(string parameterName, object value)
        {
            return new SqliteParameter(parameterName, value ?? DBNull.Value);
        }

        protected override IDbDataParameter[] InitAddDbParameters(Message message, int? position = null)
        {
            var prefix = position.HasValue ? $"p{position}_" : "";
            var bagJson = JsonSerializer.Serialize(message.Header.Bag, JsonSerialisationOptions.Options);
            return new[]
            {
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}MessageId",
                    SqliteType = SqliteType.Text,
                    Value = message.Id.ToString()
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}MessageType",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.MessageType.ToString()
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}Topic", SqliteType = SqliteType.Text, Value = message.Header.Topic
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}Timestamp",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}CorrelationId",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.CorrelationId
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}ReplyTo",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.ReplyTo
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}ContentType",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.ContentType
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}PartitionKey",
                    SqliteType = SqliteType.Text,
                    Value = message.Header.PartitionKey
                },
                new SqliteParameter
                {
                    ParameterName = $"@{prefix}HeaderBag", SqliteType = SqliteType.Text, Value = bagJson
                },
                _configuration.BinaryMessagePayload
                    ? new SqliteParameter
                    {
                        ParameterName = $"@{prefix}Body", SqliteType = SqliteType.Blob, Value = message.Body.Bytes
                    }
                    : new SqliteParameter
                    {
                        ParameterName = $"@{prefix}Body", SqliteType = SqliteType.Text, Value = message.Body.Value
                    }
            };
        }

        protected override Message MapFunction(DbDataReader dr)
        {
            using (dr)
            {
                if (dr.Read())
                {
                    return MapAMessage(dr);
                }

                return new Message();
            }
        }

        protected override async Task<Message> MapFunctionAsync(DbDataReader dr, CancellationToken cancellationToken)
        {
            using (dr)
            {
                if (await dr.ReadAsync(cancellationToken))
                {
                    return MapAMessage(dr);
                }

                return new Message();
            }
        }

        protected override IEnumerable<Message> MapListFunction(DbDataReader dr)
        {
            var messages = new List<Message>();
            while (dr.Read())
            {
                messages.Add(MapAMessage(dr));
            }

            dr.Close();

            return messages;
        }

        protected override async Task<IEnumerable<Message>> MapListFunctionAsync(DbDataReader dr, CancellationToken cancellationToken)
        {
            var messages = new List<Message>();
            while (await dr.ReadAsync(cancellationToken))
            {
                messages.Add(MapAMessage(dr));
            }

            dr.Close();

            return messages;
        }

        private static bool IsExceptionUnqiueOrDuplicateIssue(SqliteException sqlException)
        {
            return sqlException.SqliteErrorCode == SqliteDuplicateKeyError ||
                   sqlException.SqliteErrorCode == SqliteUniqueKeyError;
        }

        private Message MapAMessage(IDataReader dr)
        {
            var id = GetMessageId(dr);
            var messageType = GetMessageType(dr);
            var topic = GetTopic(dr);

            var header = new MessageHeader(id, topic, messageType);

            if (dr.FieldCount > 4)
            {
                DateTime timeStamp = GetTimeStamp(dr);
                var correlationId = GetCorrelationId(dr);
                var replyTo = GetReplyTo(dr);
                var contentType = GetContentType(dr);
                var partitionKey = GetPartitionKey(dr);

                header = new MessageHeader(
                    messageId: id,
                    topic: topic,
                    messageType: messageType,
                    timeStamp: timeStamp,
                    handledCount: 0,
                    delayedMilliseconds: 0,
                    correlationId: correlationId,
                    replyTo: replyTo,
                    contentType: contentType,
                    partitionKey: partitionKey);

                Dictionary<string, object> dictionaryBag = GetContextBag(dr);
                if (dictionaryBag != null)
                {
                    foreach (var key in dictionaryBag.Keys)
                    {
                        header.Bag.Add(key, dictionaryBag[key]);
                    }
                }
            }

            var body = _configuration.BinaryMessagePayload
                ? new MessageBody(GetBodyAsBytes((SqliteDataReader)dr), "application/octet-stream",
                    CharacterEncoding.Raw)
                : new MessageBody(dr.GetString(dr.GetOrdinal("Body")), "application/json", CharacterEncoding.UTF8);


            return new Message(header, body);
        }


        private byte[] GetBodyAsBytes(DbDataReader dr)
        {
            var i = dr.GetOrdinal("Body");
            var body = dr.GetStream(i);
            var buffer = new byte[body.Length];
            body.Read(buffer, 0, (int)body.Length);
            return buffer;
        }

        private static Dictionary<string, object> GetContextBag(IDataReader dr)
        {
            var i = dr.GetOrdinal("HeaderBag");
            var headerBag = dr.IsDBNull(i) ? "" : dr.GetString(i);
            var dictionaryBag =
                JsonSerializer.Deserialize<Dictionary<string, object>>(headerBag, JsonSerialisationOptions.Options);
            return dictionaryBag;
        }

        private string GetContentType(IDataReader dr)
        {
            var ordinal = dr.GetOrdinal("ContentType");
            if (dr.IsDBNull(ordinal)) return null;

            var contentType = dr.GetString(ordinal);
            return contentType;
        }


        private Guid? GetCorrelationId(IDataReader dr)
        {
            var ordinal = dr.GetOrdinal("CorrelationId");
            if (dr.IsDBNull(ordinal)) return null;

            var correlationId = dr.GetGuid(ordinal);
            return correlationId;
        }

        private static MessageType GetMessageType(IDataReader dr)
        {
            return (MessageType)Enum.Parse(typeof(MessageType), dr.GetString(dr.GetOrdinal("MessageType")));
        }

        private static Guid GetMessageId(IDataReader dr)
        {
            return Guid.Parse(dr.GetString(dr.GetOrdinal("MessageId")));
        }

        private string GetPartitionKey(IDataReader dr)
        {
            var ordinal = dr.GetOrdinal("PartitionKey");
            if (dr.IsDBNull(ordinal)) return null;

            var partitionKey = dr.GetString(ordinal);
            return partitionKey;
        }


        private string GetReplyTo(IDataReader dr)
        {
            var ordinal = dr.GetOrdinal("ReplyTo");
            if (dr.IsDBNull(ordinal)) return null;

            var replyTo = dr.GetString(ordinal);
            return replyTo;
        }

        private static string GetTopic(IDataReader dr)
        {
            return dr.GetString(dr.GetOrdinal("Topic"));
        }


        private static DateTime GetTimeStamp(IDataReader dr)
        {
            var ordinal = dr.GetOrdinal("Timestamp");
            var timeStamp = dr.IsDBNull(ordinal)
                ? DateTime.MinValue
                : dr.GetDateTime(ordinal);
            return timeStamp;
        }
    }
}
