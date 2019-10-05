using System;
using System.Collections.Generic;
using System.Linq;

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

    public bool TryGetMissionOf(Robot myRobot, Game game,  out Mission onGoingMission)
    {
        var hasMission = _missions.TryGetValue(myRobot.Id, out onGoingMission);

        if(hasMission == false)
        {
            return false;
        }

        if(onGoingMission.IsCompleted(myRobot, game))
        {
            onGoingMission = null;
            return false;
        }

        return true;
    }

    public Mission AssignMission(Robot myRobot, Game game)
    {
        var hasRecommendedOrePosition = TryRecommendOrePosition(game, myRobot, out Coord orePosition);

        if (hasRecommendedOrePosition == false)
        {
            var existingDigRadarMission = _missions.Values.OfType<DigRadarMission>().SingleOrDefault();

            if (existingDigRadarMission == null)
            {
                var radarPosition = game.GetRecommendRadarPosition();
                _missions[myRobot.Id] = new DigRadarMission(radarPosition);
            }
            else
            {
                _missions[myRobot.Id] = new MoveMission(existingDigRadarMission.TargetPosition);
            }
        }
        else
        {
            _missions[myRobot.Id] = new DigOreMission(orePosition);
        }

        return _missions[myRobot.Id];

    }

    private bool TryRecommendOrePosition(Game game, Robot robot, out Coord orePosition)
    {
        orePosition = null;
        
        var revealedOreCells = game
            .GetRevealedOreCells()
            .OrderBy(cell => cell.Item1.Distance(robot.Pos))
            .ToList();

        var alreadyAsssignedPositions = _missions
            .Values.OfType<DigOreMission>()
            .Select(x => x.OrePosition)
            .ToArray();

        foreach(var oreCell in revealedOreCells)
        {
            var possiblePosition = oreCell.Item1;
            var oreCount = oreCell.Item2.Ore;
            var assignedRobotCount = alreadyAsssignedPositions.Count(p => p.Distance(possiblePosition) == 0);
            var possiblePositionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(possiblePosition) == 0);

            if (assignedRobotCount == oreCount || possiblePositionHasTrap)
            {
                //Already assigned
            }
            else
            {
                orePosition = possiblePosition;
                break;
            }
        }

        return orePosition != null;

    }
}

abstract class Mission
{
    public abstract string GetAction(Robot robot, Game game);

    public abstract bool IsCompleted(Robot robot, Game game);
    
}

class MoveMission : Mission
{
    private readonly Coord _targetPosition;

    public MoveMission(Coord targetPosition)
    {
        _targetPosition = targetPosition;
    }

    public override string GetAction(Robot robot, Game game)
    {
        return Robot.Move(_targetPosition);
    }

    public override bool IsCompleted(Robot myRobot, Game game)
    {
        return myRobot.Pos.Distance(_targetPosition) == 0;
    }

    public override string ToString()
    {
        return $"Move to {_targetPosition.ToString()}";
    }
}

class DigOreMission : Mission
{
    public readonly Coord OrePosition;
    private bool _justDig = false;

    public DigOreMission(Coord orePosition)
    {
        OrePosition = orePosition;
    }

    public override string GetAction(Robot robot, Game game)
    {
        if(robot.Item == EntityType.ORE)
        {
            return Robot.Move(new Coord(0, robot.Pos.Y));
        }

        if (robot.Pos.Distance(OrePosition) <= 1)
        {
            _justDig = true;
            return Robot.Dig(OrePosition);
        }
        else
        {
            bool trapIsAvailable = game.TrapCooldown == 0;
            if (robot.IsAtHeadquerters() && trapIsAvailable)
            {
                return Robot.Request(EntityType.TRAP);
            }

            return Robot.Move(OrePosition);
        }
    }

    public override bool IsCompleted(Robot robot, Game game)
    {
        bool noMoreOre = _justDig == true && robot.Item == EntityType.NONE;
        bool positionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(OrePosition) == 0);

        return robot.IsAtHeadquerters() ||
            positionHasTrap ||
            noMoreOre;
    }

    public override string ToString()
    {
        return $"Dig ore at {OrePosition.ToString()}";
    }
}

class DigRadarMission: Mission
{
    public readonly Coord TargetPosition;
    private bool gotRadar;

    public DigRadarMission(Coord targetPosition)
    {
        TargetPosition = targetPosition;
    }

    public override string GetAction(Robot robot, Game game)
    {
        switch(robot.Item)
        {
            case EntityType.NONE:
                //Go get a radar
                return GoGetRadar(robot, game);
            case EntityType.RADAR:
                gotRadar = true;

                //Go dig radar
                return GoDigRadar(robot);

        }

        return GoToHeadquarters(robot);
    }

    private string GoGetRadar(Robot robot, Game game)
    {
        if (robot.Pos.X == 0)
        {
            game.RadarCooldown = 4;
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
        if (robot.Pos.Distance(TargetPosition) <= 1)
        {
            return Robot.Dig(TargetPosition);
        }
        else
        {
            return Robot.Move(TargetPosition);
        }
    }

    public override bool IsCompleted(Robot myRobot, Game game)
    {
        var positionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(TargetPosition) == 0);

        return (gotRadar && myRobot.Item == EntityType.NONE) || 
            positionHasTrap;
    }

    public override string ToString()
    {
        return $"Dig radar at {TargetPosition.ToString()}";
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

    public IReadOnlyList<Tuple<Coord, Cell>> GetRevealedOreCells()
    {
        var cellsWithOre = new List<Tuple<Coord, Cell>>();

        for (int x = 0; x < Width; ++x)
        {
            for (int y = 0; y < Height; ++y)
            {
                if (Cells[x, y].Ore > 0)
                {
                    cellsWithOre.Add(new Tuple<Coord, Cell>(new Coord(x,y), Cells[x, y]));
                }
            }
        }

        return cellsWithOre;
    }

    public Coord GetRecommendRadarPosition()
    {
        var recommendedRadarPositions = GetRecommendedRadarPositions();
        var myRadarPositions = this.Radars.Select(radar => radar.Pos).ToList();

        foreach (var recommendedPosition in recommendedRadarPositions)
        {
            var positionHasAlreadyARadar = myRadarPositions.Any(p => p.Distance(recommendedPosition) == 0);
            var positionHasATrap = Traps.Any(trap => trap.Pos.Distance(recommendedPosition) == 0);

            if (positionHasAlreadyARadar || positionHasATrap)
            {
                //Go to next
            }
            else
            {
                return recommendedPosition;
            }
        }

        return recommendedRadarPositions[0];
    }

    private Coord[] GetRecommendedRadarPositions()
    {
        return new List<Coord>
        {
            new Coord(9, 7),
            new Coord(13,3),
            new Coord(13,11),
            new Coord(17,7),
            new Coord(5, 3),
            new Coord(5, 11),
            new Coord(21,3),
            new Coord(21,11),
            new Coord(25,7)
        }.ToArray();
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

    public override string ToString()
    {
        return $"({X.ToString()},{Y.ToString()})";
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
            var hasMission = _onGoingMissions.TryGetMissionOf(myRobot, _game, out onGoingMission);

            if(hasMission == false)
            {
                onGoingMission = _onGoingMissions.AssignMission(myRobot, _game);
            }

            Player.Debug($"Robot {myRobot.Id} on mission {onGoingMission.ToString()}");

            actions.Add(onGoingMission.GetAction(myRobot, _game));
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

            game.RadarCooldown = radarCooldown;
            game.TrapCooldown = trapCooldown;

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