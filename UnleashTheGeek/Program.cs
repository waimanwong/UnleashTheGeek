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

class OnGoingMissions
{
    /// <summary>
    /// Mission by robot id
    /// </summary>
    private Dictionary<int, Mission> _missions;

    public OnGoingMissions()
    {
        _missions = new Dictionary<int, Mission>();
    }

    public bool TryGetMissionOf(Robot myRobot, out Mission onGoingMission)
    {
        var hasMission = _missions.TryGetValue(myRobot.Id, out onGoingMission);

        if(hasMission == false)
        {
            return false;
        }

        if(onGoingMission.IsCompleted(myRobot))
        {
            onGoingMission = null;
            return false;
        }

        return true;
    }

    public Mission AssignMission(Robot myRobot, Game game)
    {
        if(myRobot.IsAtHeadquerters())
        {
            if(game.RadarCooldown == 0)
            {
                _missions[myRobot.Id] = new DigRadar(new Coord(5,3));
            }
            else
            {
                _missions[myRobot.Id] = new Move(new Coord(5, 3));
            }
        }
        else
        {
            _missions[myRobot.Id] = new Move(new Coord(0, myRobot.Pos.Y));
        }

        return _missions[myRobot.Id];
    }

}

abstract class Mission
{
    public abstract string GetAction(Robot robot);

    public abstract bool IsCompleted(Robot myRobot);
    
}

class Move : Mission
{
    private readonly Coord _targetPosition;

    public Move(Coord targetPosition)
    {
        _targetPosition = targetPosition;
    }

    public override string GetAction(Robot robot)
    {
        return Robot.Move(_targetPosition);
    }

    public override bool IsCompleted(Robot myRobot)
    {
        return myRobot.Pos == _targetPosition;
    }
}

class DigRadar: Mission
{
    private readonly Coord _targetPosition;
    private bool gotRadar;

    public DigRadar(Coord targetPosition)
    {
        _targetPosition = targetPosition;
    }

    public override string GetAction(Robot robot)
    {
        if (robot.Item == EntityType.NONE)
        {
            //Go get a radar
            return GoGetRadar(robot);
        }
        else
        {
            gotRadar = true;

            //Go dig radar
            return GoDigRadar(robot);
        }
    }

    private string GoGetRadar(Robot robot)
    {
        if (robot.Pos.X == 0)
        {
            return Robot.Request(EntityType.RADAR);
        }
        else
        {
            return GoToHeadquarters(robot);
        }
    }

    private string GoToHeadquarters(Robot robot)
    {
        return Robot.Move(new Coord(0, robot.Pos.Y));
    }

    private string GoDigRadar(Robot robot)
    {
        if (robot.Pos.Distance(_targetPosition) <= 1)
        {
            return Robot.Dig(_targetPosition);
        }
        else
        {
            return Robot.Move(_targetPosition);
        }
    }

    public override bool IsCompleted(Robot myRobot)
    {
        return gotRadar && myRobot.Item == EntityType.NONE;
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

    public Game(int width, int height)
    {
        Width = width;
        Height = height;
        MyRobots = new List<Robot>();
        OpponentRobots = new List<Robot>();
        Cells = new Cell[width, height];
        Radars = new List<Entity>();
        Traps = new List<Entity>();

        InitializeCellContent();
    }

    private void InitializeCellContent()
    {
        for (int x = 0; x < Width; ++x)
        {
            for (int y = 0; y < Height; ++y)
            {
                Cells[x, y] = new Cell();
            }
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

    public bool IsAtHeadquerters()
    {
        return this.Pos.X == 0;
    }
}

class AI
{
    private readonly Game _game;
    private readonly OnGoingMissions _onGoingMissions;

    public AI(Game game, OnGoingMissions onGoingMissions)
    {
        _game = game;
        _onGoingMissions = onGoingMissions;
    }

    public string[] GetActions()
    {   
        var actions = new List<string>();

        foreach(var myRobot in _game.MyRobots)
        {
            Mission onGoingMission;
            var hasMission = _onGoingMissions.TryGetMissionOf(myRobot, out onGoingMission);

            if(hasMission == false)
            {
                onGoingMission = _onGoingMissions.AssignMission(myRobot, _game);
            }

            actions.Add(onGoingMission.GetAction(myRobot));

        }

        return actions.ToArray();
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
    OnGoingMissions onGoingMissions = new OnGoingMissions();
    
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

            var ai = new AI(game, onGoingMissions);
            var actions = ai.GetActions();

            foreach(var action in actions)
            {
                Console.WriteLine(action);
            }
        }
    }
}