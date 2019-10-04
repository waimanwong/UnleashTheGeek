using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;


enum EntityType
{
    NONE = -1, MY_ROBOT = 0, OPPONENT_ROBOT = 1, RADAR = 2, TRAP = 3, ORE = 4
}

class Cell
{
    public int Ore { get; set; }
    public bool Hole { get; set; }
    public bool Known { get; set; }

    public void Update(string ore, int hole)
    {
        Hole = hole == 1;
        Known = !"?".Equals(ore);
        if (Known)
        {
            Ore = int.Parse(ore);
        }
    }
}

class Game
{
    // Given at startup
    public readonly int Width;
    public readonly int Height;

    // Updated each turn
    public List<Robot> MyRobots { get; set; }
    public List<Robot> OpponentRobots { get; set; }
    public Cell[,] Cells { get; set; }
    public int RadarCooldown { get; set; }
    public int TrapCooldown { get; set; }
    public int MyScore { get; set; }
    public int OpponentScore { get; set; }
    public List<Entity> Radars { get; set; }
    public List<Entity> Traps { get; set; }

    public Queue<Coord> RadarTargetPosition { get; private set; }

    public Game(int width, int height)
    {
        Width = width;
        Height = height;
        MyRobots = new List<Robot>();
        OpponentRobots = new List<Robot>();
        Cells = new Cell[width, height];
        Radars = new List<Entity>();
        Traps = new List<Entity>();

        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                Cells[x, y] = new Cell();
            }
        }

        RadarTargetPosition = new Queue<Coord>();
        int targetX = 1;
        while (targetX < Width)
        {
            int targetY = 4;

            while (targetY < Height)
            {
                RadarTargetPosition.Enqueue(new Coord(targetX, targetY));

                targetY += 5;
            }

            targetX += 5;
        }

    }

}

class Coord
{
    public static readonly Coord NONE = new Coord(-1, -1);

    public int X { get; }
    public int Y { get; }

    public Coord(int x, int y)
    {
        X = x;
        Y = y;
    }

    // Manhattan distance (for 4 directions maps)
    // see: https://en.wikipedia.org/wiki/Taxicab_geometry
    public int Distance(Coord other)
    {
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    }

    public override bool Equals(object obj)
    {
        if (obj == null) return false;
        if (this.GetType() != obj.GetType()) return false;
        Coord other = (Coord)obj;
        return X == other.X && Y == other.Y;
    }

    public override int GetHashCode()
    {
        return 31 * (31 + X) + Y;
    }
}

class Entity
{
    public int Id { get; set; }
    public Coord Pos { get; set; }
    public EntityType Item { get; private set; }

    public Entity(int id, Coord pos, EntityType item)
    {
        Id = id;
        Pos = pos;
        Item = item;
    }

    public override string ToString()
    {
        return $"Entity {Id.ToString()} at ({Pos.X.ToString()},{Pos.Y.ToString()}) holding {Item.ToString()}";
    }
}

class Robot : Entity
{
    public Robot(int id, Coord pos, EntityType item) : base(id, pos, item)
    {
    }

    bool IsDead()
    {
        return Pos.Equals(Coord.NONE);
    }

    public static string Wait(string message = "")
    {
        return $"WAIT {message}";
    }

    public static string Move(Coord pos, string message = "")
    {
        return $"MOVE {pos.X} {pos.Y} {message}";
    }

    public static string Dig(Coord pos, string message = "")
    {
        return $"DIG {pos.X} {pos.Y} {message}";
    }

    public static string Request(EntityType item, string message = "")
    {
        return $"REQUEST {item.ToString()} {message}";
    }
}

class AI
{
    private readonly Game _game;

    public AI(Game game)
    {
        _game = game;
    }

    public string[] GetActions()
    {   
        foreach(var robot in _game.MyRobots )
        {
            Player.Debug(robot.ToString());
        }

        var actions = new List<string>();

        //Robot 0: Get radars and dig anywhere
        actions.Add(DigRadars(_game.MyRobots[0]));



        //All others wait
        for (int i = 1; i < 5; i++)
        {
            // To debug: Console.Error.WriteLine("Debug messages...");

            Robot robot = _game.MyRobots[i];
            actions.Add( Robot.Wait("C# Starter"));

            // Implement action selection logic here.

            // WAIT|MOVE x y|REQUEST item|DIG x y
           
        }

        return actions.ToArray();
    }

    private string DigRadars(Robot robot)
    {
        if (robot.Item == EntityType.NONE)
        {
            //Go get a radar
            return GoGetRadar(robot);
        }
        else
        {
            //Go dig radar
            return GoDigRadar(robot);
        }
    }

    private string GoGetRadar(Robot robot)
    {
        if(robot.Pos.X == 0)
        {
            return Robot.Request(EntityType.RADAR);
        }
        else
        {
            return Robot.Move(new Coord(0, robot.Pos.Y));
        }
    }

    private string GoDigRadar(Robot robot)
    {
        Coord targetPosition = _game.RadarTargetPosition.Peek();

        if (robot.Pos.Distance(targetPosition) <= 1)
        {
            _game.RadarTargetPosition.Dequeue();

            return Robot.Dig(targetPosition);
        }
        else
        {
            return Robot.Move(targetPosition);
        }
    }
}

/**
 * Deliver more ore to hq (left side of the map) than your opponent. Use radars to find ore but beware of traps!
 **/
class Player
{
    public static void Debug(string message)
    {
        Console.Error.WriteLine(message);
    }

    static void Main(string[] args)
    {
        new Player();
    }

    Game game;

    public Player()
    {
        string[] inputs;
        inputs = Console.ReadLine().Split(' ');
        int width = int.Parse(inputs[0]);
        int height = int.Parse(inputs[1]); // size of the map

        game = new Game(width, height);

        // game loop
        while (true)
        {
            inputs = Console.ReadLine().Split(' ');
            game.MyScore = int.Parse(inputs[0]); // Amount of ore delivered
            game.OpponentScore = int.Parse(inputs[1]);
            for (int i = 0; i < height; i++)
            {
                var row = Console.ReadLine();

                inputs = row.Split(' ');
                for (int j = 0; j < width; j++)
                {
                    string ore = inputs[2 * j];// amount of ore or "?" if unknown
                    int hole = int.Parse(inputs[2 * j + 1]);// 1 if cell has a hole
                    game.Cells[j, i].Update(ore, hole);
                }
            }
            inputs = Console.ReadLine().Split(' ');
            int entityCount = int.Parse(inputs[0]); // number of entities visible to you
            int radarCooldown = int.Parse(inputs[1]); // turns left until a new radar can be requested
            int trapCooldown = int.Parse(inputs[2]); // turns left until a new trap can be requested

            game.Radars.Clear();
            game.Traps.Clear();
            game.MyRobots.Clear();
            game.OpponentRobots.Clear();

            for (int i = 0; i < entityCount; i++)
            {
                var entityState = Console.ReadLine();

                inputs = entityState.Split(' ');
                int id = int.Parse(inputs[0]); // unique id of the entity
                EntityType type = (EntityType)int.Parse(inputs[1]); // 0 for your robot, 1 for other robot, 2 for radar, 3 for trap
                int x = int.Parse(inputs[2]);
                int y = int.Parse(inputs[3]); // position of the entity
                EntityType item = (EntityType)int.Parse(inputs[4]); // if this entity is a robot, the item it is carrying (-1 for NONE, 2 for RADAR, 3 for TRAP, 4 for ORE)
                Coord coord = new Coord(x, y);

                switch (type)
                {
                    case EntityType.MY_ROBOT:
                        game.MyRobots.Add(new Robot(id, coord, item));
                        break;
                    case EntityType.OPPONENT_ROBOT:
                        game.OpponentRobots.Add(new Robot(id, coord, item));
                        break;
                    case EntityType.RADAR:
                        game.Radars.Add(new Entity(id, coord, item));
                        break;
                    case EntityType.TRAP:
                        game.Traps.Add(new Entity(id, coord, item));
                        break;
                }
            }

            var ai = new AI(game);
            var actions = ai.GetActions();

            foreach(var action in actions)
            {
                Console.WriteLine(action);
            }
        }
    }
}