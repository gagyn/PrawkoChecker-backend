using System.Collections.Generic;
using LiteDB;
using PrawkoChecker.Models;
using Serilog;

namespace PrawkoChecker.Services
{
    public class DbService
    {
        private LiteDatabase GetDatabase => new(@"db/database.db");

        public void AddOrUpdate<T>(T value) where T : Entity
        {
            using var db = this.GetDatabase;
            Log.Information("Try to get {name} collection from db (for addOrUpdate)", typeof(T).Name);
            var collection = db.GetCollection<T>($"{typeof(T).Name}s");
            collection.Insert(value);
        }

        public ICollection<T> Get<T>() where T : Entity
        {
            using var db = this.GetDatabase;
            Log.Information("Try to get {name} collection from db", typeof(T).Name);
            var collection = db.GetCollection<T>($"{typeof(T).Name}s");
            return collection.Query().ToArray();
        }

        public bool Delete<T>(T value) where T : Entity
        {
            using var db = this.GetDatabase;
            Log.Information("Try to get {name} collection from db (for delete)", typeof(T).Name);
            var collection = db.GetCollection<T>($"{typeof(T).Name}s");
            return collection.Delete(value.Id);
        }
    }
}
