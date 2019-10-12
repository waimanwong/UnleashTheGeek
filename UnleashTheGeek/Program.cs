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
    private Dictionary<int, Mission> _missionsByRobot;

    public OnGoingMissions()
    {
        _missionsByRobot = new Dictionary<int, Mission>();
    }

    public bool TryGetMissionOf(Robot myRobot, Game game, out Mission onGoingMission)
    {
        var hasMission = _missionsByRobot.TryGetValue(myRobot.Id, out onGoingMission);

        if (hasMission == false)
        {
            return false;
        }

        if (onGoingMission.IsCompleted(myRobot, game))
        {
            onGoingMission = null;
            return false;
        }

        return true;
    }

    public Mission AssignMission(Robot robot, Mission mission)
    {
        _missionsByRobot[robot.Id] = mission;
        return mission;
    }

    public Mission AssignMission(Robot myRobot, Game game)
    {
        //if (_missionsByRobot.Values.OfType<RobotKillerMission>().Any() == false)
        //{
        //    //Only one suicide mission
        //    if (game.TrapCooldown == 0 && game.MyRobots.Count <= game.OpponentRobots.Count)
        //    {
        //        _missionsByRobot[myRobot.Id] = new RobotKillerMission();
        //        return _missionsByRobot[myRobot.Id];
        //    }
        //}

        //Mission to radar        
        if (game.GetRevealedOreCells().Count <= 7 && game.RadarCooldown == 0)
        {
            var onGoingRadarMissionLocations = _missionsByRobot.Values
                .OfType<DigRadarMission>()
                .Select(m => m.TargetPosition)
                .ToList();

            var radarPosition = game.GetRecommendRadarPosition(onGoingRadarMissionLocations);
            _missionsByRobot[myRobot.Id] = new DigRadarMission(radarPosition);

            game.RadarCooldown = 4; //so other robot will not be assigned to dig a radar

            return _missionsByRobot[myRobot.Id];
        }

        //Mission to dig ore
        if (TryRecommendOrePosition(game, myRobot, out Coord orePosition))
        {
            _missionsByRobot[myRobot.Id] = new DigOreMission(orePosition);
            return _missionsByRobot[myRobot.Id];
        }

        //Random dig ore
        var randomPosition = new Coord((myRobot.Pos.X + 5) % game.Width, myRobot.Pos.Y);
        _missionsByRobot[myRobot.Id] = new DigOreMission(randomPosition);
        return _missionsByRobot[myRobot.Id];

    }

    private bool TryRecommendOrePosition(Game game, Robot robot, out Coord orePosition)
    {
        orePosition = null;

        var revealedOreCells = game
            .GetRevealedOreCells()
            .OrderBy(cell => cell.Item1.Distance(robot.Pos))
            .ToList();

        var alreadyAsssignedPositions = _missionsByRobot
            .Values.OfType<DigOreMission>()
            .Select(x => x.OrePosition)
            .ToArray();

        foreach (var oreCell in revealedOreCells)
        {
            var possiblePosition = oreCell.Item1;
            var oreCount = oreCell.Item2.Ore;
            var assignedRobotCount = alreadyAsssignedPositions.Count(p => p.Distance(possiblePosition) == 0);
            var possiblePositionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(possiblePosition) == 0);
            var isHoleDiggedByOpponent =
                game.IsAHole(possiblePosition) == true &&
                game.HolesDiggedByMe.ContainsKey(possiblePosition.GetHashCode()) == false;

            if (assignedRobotCount == oreCount || possiblePositionHasTrap || isHoleDiggedByOpponent)
            {
                //ignore this
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

    public bool DoTriggerTrap(Robot robot, Game game, out string action)
    {
        action = null;
        var trapInRange = game.Traps.FirstOrDefault(t => t.Pos.Distance(robot.Pos) <= 1);
        if (trapInRange == null)
            return false;

        var opponentRobotsCountInRange = game.OpponentRobots.Count(r => r.Pos.Distance(trapInRange.Pos) <= 1);
        if(opponentRobotsCountInRange < 2)
        {
            return false;
        }

        action = Robot.Dig(trapInRange.Pos, game);

        return true;
    }

}


class RobotKillerMission : Mission
{
    private bool _justDigged = false;

    public override string GetAction(Robot robot, Game game)
    {
        if (robot.IsAtHeadquerters() == false)
        {
            return Robot.Move(new Coord(0, robot.Pos.Y));
        }

        //Do trigger ?
        if (game.Traps.Any(t => t.Pos.Y == robot.Pos.Y))
        {
            var trapsMinY = game.Traps.Min(t => t.Pos.Y);
            var trapsMaxY = game.Traps.Max(t => t.Pos.Y);

            Func<Robot, bool> inRange =
                r => (r.Pos.X <= 2 && trapsMinY <= r.Pos.Y && r.Pos.Y <= trapsMaxY) ||
                        (r.Pos.X == 1 && trapsMinY - 1 == r.Pos.Y) ||
                        (r.Pos.X == 1 && trapsMinY + 1 == r.Pos.Y);

            var enemyInRangeCount = game.OpponentRobots.Count(inRange);
            var mineInRangeCount = game.MyRobots.Count(inRange);

            if (enemyInRangeCount >= 2 + mineInRangeCount)
            {
                return Robot.Dig(new Coord(1, robot.Pos.Y), game);
            }
        }
                

        if (game.TrapCooldown == 0)
        {
            if (robot.Item == EntityType.NONE)
            {
                return PickTrap(game);
            }
            else
            {
                //Carrying stuff
                return DigToEmptyCell(robot, game);
            }
        }
        else
        {
            if (robot.Item == EntityType.NONE)
            {
                return Robot.Wait();
            }
            else
            {
                //Carrying stuff
                return DigToEmptyCell(robot, game);
            }
        }
    }

    private string DigToEmptyCell(Robot robot, Game game)
    {
        if(game.Traps.Any(t => t.Pos.Y == robot.Pos.Y))
        {
            return GotoSafeCell(robot, game);
        }
        else
        {
            return Robot.Dig(new Coord(1, robot.Pos.Y), game);
        }
    }

    private string GotoSafeCell(Robot robot, Game game)
    {
        var unsafeYs = game.Traps.Select(t => t.Pos.Y).ToHashSet();

        int selectedY = Enumerable.Range(1, game.Height - 2)
            .Where(y => unsafeYs.Contains(y) == false)
            .OrderBy(y => Math.Abs(robot.Pos.Y - y))
            .FirstOrDefault();

        if (selectedY == 0)
        {
            return Robot.Wait();
        }
        else
        {
            return Robot.Move(new Coord(0, selectedY));
        }        
    }

    private string PickTrap(Game game)
    {
        game.TrapCooldown = 4;
        return Robot.Request(EntityType.TRAP);
    }

    public override bool IsCompleted(Robot robot, Game game)
    {
        return game.OpponentRobots.Count < game.MyRobots.Count;
    }
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
        if (DoTriggerTrap(robot, game, out string action))
            return action;

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
    public Coord OrePosition;
    private bool _justDig = false;
    private bool _carryingOre = false;

    public DigOreMission(Coord orePosition)
    {
        OrePosition = orePosition;
    }

    public override string GetAction(Robot robot, Game game)
    {
        if (DoTriggerTrap(robot, game, out string action))
            return action;

        if (robot.Item == EntityType.ORE)
        {
            _carryingOre = true;
            return Robot.Move(new Coord(0, robot.Pos.Y));
        }

        if (robot.Pos.Distance(OrePosition) <= 1)
        {
            _justDig = true;
            return Robot.Dig(OrePosition, game);
        }
        else
        {
            bool trapIsAvailable = game.TrapCooldown == 0;
            if (robot.IsAtHeadquerters() && trapIsAvailable)
            {
                game.TrapCooldown = 4;
                return Robot.Request(EntityType.TRAP);
            }

            bool radarIsAvailable = game.RadarCooldown == 0;
            if (robot.IsAtHeadquerters() && radarIsAvailable)
            {
                game.RadarCooldown = 4;
                return Robot.Request(EntityType.RADAR);
            }

            var opponentHasDiggedAHole = game.OpponentHasDiggedAHole(OrePosition);
            if (opponentHasDiggedAHole)
            {
                OrePosition = new Coord(OrePosition.X, OrePosition.Y + 1);
            }
            return Robot.Move(new Coord( OrePosition.X - 1, OrePosition.Y));
        }
    }

    public override bool IsCompleted(Robot robot, Game game)
    {
        bool noMoreOre = _justDig == true && robot.Item == EntityType.NONE;
        bool positionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(OrePosition) == 0);
        
        return robot.IsAtHeadquerters() && _carryingOre ||
            positionHasTrap ||
            noMoreOre;
    }

    public override string ToString()
    {
        return $"Dig ore at {OrePosition.ToString()}";
    }
}

class DigRadarMission : Mission
{
    public Coord TargetPosition;
    private bool gotRadar;

    public DigRadarMission(Coord targetPosition)
    {
        TargetPosition = targetPosition;
    }

    public override string GetAction(Robot robot, Game game)
    {
        if (DoTriggerTrap(robot, game, out string action))
            return action;

        switch (robot.Item)
        {
            case EntityType.NONE:
                //Go get a radar
                return GoGetRadar(robot, game);
            case EntityType.RADAR:
                gotRadar = true;

                //Go dig radar
                return GoDigRadar(robot, game);

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

    private string GoDigRadar(Robot robot, Game game)
    {
        if (robot.Pos.Distance(TargetPosition) <= 1)
        {
            var opponentHasDiggedAHole = game.OpponentHasDiggedAHole(TargetPosition);
            var positionHasTrap = game.Traps.Any(trap => trap.Pos.Distance(TargetPosition) == 0);

            if (opponentHasDiggedAHole || positionHasTrap)
            {
                //ChangePosition
                TargetPosition = new Coord(TargetPosition.X - 1, TargetPosition.Y);
            }
            return Robot.Dig(TargetPosition, game);
        }
        else
        {
            return Robot.Move(TargetPosition);
        }
    }

    public override bool IsCompleted(Robot myRobot, Game game)
    {
        var justDroppedARadar = gotRadar && myRobot.Item == EntityType.NONE;

        return justDroppedARadar;
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

    public Dictionary<int,Coord> HolesDiggedByMe { get; private set; }

    public Game(int width, int height)
    {
        Width = width;
        Height = height;
        MyRobots = new List<Robot>();
        OpponentRobots = new List<Robot>();
        Cells = new Cell[width, height];
        Radars = new List<Entity>();
        Traps = new List<Entity>();
        HolesDiggedByMe = new Dictionary<int, Coord>();

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
                if (Cells[x, y].Known && Cells[x, y].Ore > 0)
                {
                    cellsWithOre.Add(new Tuple<Coord, Cell>(new Coord(x, y), Cells[x, y]));
                }
            }
        }

        return cellsWithOre;
    }

    public Coord GetRecommendRadarPosition(IEnumerable<Coord> futurRadarPositions)
    {
        var myRadarPositions = this.Radars.Select(radar => radar.Pos).ToList();
        myRadarPositions.AddRange(futurRadarPositions);
        
        var recommendedRadarPositions = GetRecommendedRadarPositions();
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

    public void RegisterMyHole(Coord holeCoord)
    {
        HolesDiggedByMe[holeCoord.GetHashCode()] = holeCoord;
    }

    public bool OpponentHasDiggedAHole(Coord position)
    {
        return IsAHole(position) &&
            HolesDiggedByMe.ContainsKey(position.GetHashCode()) == false;
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

    public void Debug()
    {
        var knowns = new List<Coord>();
        var unknowns = new List<Coord>();

        var opponentHoles = new List<Coord>();

        for (int x = 0; x < Width; ++x)
        {
            for (int y = 0; y < Height; ++y)
            {
                var coord = new Coord(x, y);

                if(Cells[x,y].Hole)
                {
                    if(HolesDiggedByMe.ContainsKey(coord.GetHashCode()) == false)
                    {
                        opponentHoles.Add(coord);
                    }
                }

                if (Cells[x, y].Known)
                {
                    knowns.Add(coord);
                }
                else
                {
                    unknowns.Add(coord);
                }
            }
        }

        //Player.Debug("****** Holes *********");
        //foreach (var coord in holes)
        //{
        //    Player.Debug($"Hole {coord.ToString()}");
        //}

        //Player.Debug("****** Knowns *********");
        //foreach (var coord in knowns)
        //{
        //    Player.Debug($"Knwon {coord.ToString()}");
        //}

        //Player.Debug("****** UnKnowns *********");
        //foreach (var coord in unknowns)
        //{
        //    Player.Debug($"Unknown {coord.ToString()}");
        //}
    }

    public bool IsAHole(Coord possiblePosition)
    {
        return Cells[possiblePosition.X, possiblePosition.Y].Hole == true;
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

    public static string Dig(Coord pos, Game game, string message = "")
    {
        game.RegisterMyHole(pos);

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
        //_game.Debug();

        var actions = new List<string>();
        
        if(_game.MyRobots.All(r => r.Pos.X == 0))
        {
            //Start of the game
            var radarPosition = _game.GetRecommendRadarPosition(new List<Coord>());
            var closestRobot = _game.MyRobots
                .OrderBy(r => r.Pos.Distance(radarPosition))
                .First();

            _onGoingMissions.AssignMission(closestRobot, new DigRadarMission(radarPosition));
            _game.RadarCooldown = 4;
        }


        foreach (var myRobot in _game.MyRobots)
        {
            Mission onGoingMission;
            var hasMission = _onGoingMissions.TryGetMissionOf(myRobot, _game, out onGoingMission);

            //Player.Debug($"Player {myRobot.Id} has mission {hasMission}");

            if (hasMission == false)
            {
                onGoingMission = _onGoingMissions.AssignMission(myRobot, _game);
            }

            //Player.Debug($"Player {myRobot.Id}: {onGoingMission.ToString()}");

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

            foreach (var action in actions)
            {
                Console.WriteLine(action);
            }
        }
    }
}