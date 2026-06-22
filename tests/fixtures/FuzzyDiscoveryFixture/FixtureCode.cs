namespace Alpha
{
    public partial class EnemyManager
    {
        public void Spawn()
        {
            Helper();
        }

        private void Helper()
        {
        }

        public class NestedEnemy
        {
        }
    }

    public partial class EnemyManager
    {
        public int Count { get; set; }
    }

    public sealed class EnemyManagerTools
    {
        public void Use(EnemyManager manager)
        {
            manager.Spawn();
        }

        public void EnemyManager()
        {
        }
    }

    public sealed class Spawner
    {
        public void Run()
        {
            var manager = new EnemyManager();
            manager.Spawn();
        }
    }
}

namespace Beta
{
    public sealed class EnemyManager
    {
    }

    public sealed class enemyManager
    {
    }
}
