using Microsoft.Extensions.DependencyInjection;

namespace Yu.Communication.Server
{
    public class RedisClients
    {
        private static FreeRedis.RedisClient[] _dbsFreeRedis;
        /// <summary>
        /// 多库实例 FreeRedis
        /// </summary>
        public static FreeRedis.RedisClient[] Dbs
        {
            get
            {
                if (_dbsFreeRedis == null)
                {
                    throw new Exception("使用前请初始化");
                }
                return _dbsFreeRedis;
            }
        }

        /// <summary>
        /// [按需]多库实例化 FreeRedis
        /// </summary>
        /// <param name="connectionString">格式："127.0.0.1:6379,password=123,poolsize=10"，不含defaultDatabase</param>
        /// <param name="dbidx">db索引集合，CSRedisCore默认每个库初始化5个连接</param>
        public static void Initialization(string connectionString, IServiceCollection? services = null, params int[] dbidxs)
        {
            var idxs = dbidxs == null || dbidxs.Length == 0 ? Enumerable.Range(0, 16).ToArray() : dbidxs.Distinct().ToArray();
            var dbsTmp = new FreeRedis.RedisClient[16];
            foreach (var idx in idxs)
            {
                connectionString = connectionString.Replace($",defaultDatabase={idx}", "");
                connectionString = connectionString.Replace($"defaultDatabase={idx}", "");
                dbsTmp[idx] = new FreeRedis.RedisClient($"{connectionString},defaultDatabase={idx}");
            }
            _dbsFreeRedis = dbsTmp;
            services?.AddSingleton(_dbsFreeRedis);
        }
    }
}
