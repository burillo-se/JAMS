/*
 * JAMS v1.11beta2
 *
 * (JAMS Airlock Management System)
 *
 * Published under "do whatever you want with it" license (aka public domain).
 *
 */

const string VERSION = "1.11beta2";

public List<JAMS_Group> active_airlocks = new List<JAMS_Group>();
public List<JAMS_Group> airlocks = new List<JAMS_Group>();
public List<IMyCubeGrid> local_grids = new List<IMyCubeGrid>();

// state machine
Action [] states = null;

int current_state;

/*
 * Graph-based grid locality code transplanted from BARABAS.
 */

// grid graph edge class, represents a connection point between two grids.
public class Edge < T > {
 public T src {get; set;}
 public T dst {get; set;}
}

// comparer for graph edges - the way the comparison is done means the edges are
// bidirectional - meaning, it doesn't matter which grid is source and which
// grid is destination, they will be equal as far as comparison is concerned.
public class EdgeComparer < T > : IEqualityComparer < Edge < T > > {
 public int GetHashCode(Edge < T > e) {
  // multiply src hashcode by dst hashcode - multiplication is commutative, so
  // result will be the same no matter which grid was source or destination
  return e.src.GetHashCode() * e.dst.GetHashCode();
 }
 public bool Equals(Edge < T > e1, Edge < T > e2) {
  if (e1.src.Equals(e2.src) && e1.dst.Equals(e2.dst)) {
   return true;
  }
  if (e1.src.Equals(e2.dst) && e1.dst.Equals(e2.src)) {
   return true;
  }
  return false;
 }
}

// our grid graph
public class Graph < T > {
 public Graph() {
  cmp = new EdgeComparer < T >();
  v_edges = new Dictionary < T, HashSet < Edge < T > > >();
  r_edges = new HashSet < Edge < T > >(cmp);
 }

 // add an edge to the graph
 public void addEdge(T src, T dst, bool is_remote) {
  var t = new Edge<T>();
  t.src = src;
  t.dst = dst;

  // remote edges don't need to be added to local list of edges
  if (is_remote) {
   r_edges.Add(t);
   return;
  }

  // add edge to list of per-vertex edges
  HashSet < Edge < T > > hs_src, hs_dst;
  if (!v_edges.TryGetValue(src, out hs_src)) {
   hs_src = new HashSet < Edge < T > >(cmp);
   v_edges.Add(src, hs_src);
  }
  if (!v_edges.TryGetValue(dst, out hs_dst)) {
   hs_dst = new HashSet < Edge < T > >(cmp);
   v_edges.Add(dst, hs_dst);
  }
  hs_src.Add(t);
  hs_dst.Add(t);
 }

 // get all grids that are local to source grid (i.e. all grids connected by
 // rotors or pistons)
 public List < T > getGridRegion(T src) {
  // if there never was a local edge from/to this grid, it's by definition
  // the only grid in this region
  if (!v_edges.ContainsKey(src)) {
   return new List < T >() {src};
  }
  // otherwise, gather all vertices in this region
  var region = new List<T>();
  var seen = new HashSet<T>();
  var next = new Queue<T>();
  next.Enqueue(src);
  while (next.Count != 0) {
   var g = next.Dequeue();
   if (!seen.Contains(g)) {
    var edges = v_edges[g];
    foreach (var edge in edges) {
     next.Enqueue(edge.src);
     next.Enqueue(edge.dst);
    }
    seen.Add(g);
    region.Add(g);
   }
  }
  return region;
 }

 // this must be called after adding all edges. what this does is, it removes
 // edges that aren't supposed to be there. For example, if you have grids
 // A, B, C, local edges A->B and B->C, and a remote edge C->A, there is a path
 // from C to A through local edges, so the remote edge should not count as an
 // actual "remote" edge, and therefore should be removed.
 public void validateGraph() {
  var to_remove = new HashSet < Edge <T> >(cmp);
  var seen = new HashSet<T>();
  foreach (var edge in r_edges) {
   var next = new Queue<T>();
   next.Enqueue(edge.src);
   next.Enqueue(edge.dst);
   while (next.Count != 0) {
    var g = next.Dequeue();
    if (!seen.Contains(g)) {
     var region = new HashSet<T>(getGridRegion(g));
     seen.UnionWith(region);
     // find any edges that are completely inside this region, and remove them
     foreach (var e in r_edges) {
      if (region.Contains(e.src) && region.Contains(e.dst)) {
       to_remove.Add(e);
      }
     }
    }
   }
  }
  foreach (var e in to_remove) {
   r_edges.Remove(e);
  }
 }

 // our comparer to use with all sets
 EdgeComparer < T > cmp;
 // list of all edges
 HashSet < Edge < T > > r_edges;
 // dictionaries of edges for each vertex
 Dictionary < T, HashSet < Edge < T > > > v_edges;
}

public abstract class JAMS_Group {
 public abstract void updateFromGroup(IMyBlockGroup g);
 public abstract bool updateFromString(string s);
 public abstract bool tryActivate(); // check if group should be activated
 public abstract bool finished(); // check if airlock can be retired
 protected abstract bool advanceStateImpl(); // move between states
 public abstract void reset(); // reset the airlock
 public override abstract string ToString(); // return a string representation of this group
 public string name; // name of the group
 public Program p; // reference to our programmable block
 protected TimeSpan elapsed; // timestamp when the state has started
 public abstract bool isValid(); // check if all blocks in the airlock are still valid
 public bool advanceState() {
  if (finished()) {
   return false;
  }
  bool result = advanceStateImpl();
  if (!result) {
   elapsed += p.Runtime.TimeSinceLastRun;
  }
  return result;
 }
 protected void setColor(List<IMyLightingBlock> lights, Color c) {
  if (p == null) {
   return;
  }
  p.setColor(lights, c);
 }
 protected void close(IMyDoor door) {
  if (p == null) {
   return;
  }
  p.close(door);
 }
 protected void open(IMyDoor door) {
  if (p == null) {
   return;
  }
  p.open(door);
 }
 protected void pressurize(List<IMyAirVent> vents) {
  if (p == null) {
   return;
  }
  foreach (var vent in vents) {
   p.pressurize(vent);
  }
 }
 protected void depressurize(List<IMyAirVent> vents) {
  if (p == null) {
   return;
  }
  foreach (var vent in vents) {
   p.depressurize(vent);
  }
 }
 protected void turnOffLights(List<IMyLightingBlock> lights) {
  if (p == null) {
   return;
  }
  p.turnOffLights(lights);
 }
 protected IMySlimBlock slimBlock(IMyTerminalBlock block) {
  if (p == null) {
   return null;
  }
  return p.slimBlock(block);
 }
 protected bool blockExists(IMyTerminalBlock block) {
  if (p == null) {
   // assume block exists
   return true;
  }
  return p.blockExists(block);
 }
 protected float curOxygenLevel(List<IMyAirVent> vents) {
  float cur = 0, total = 0;
  foreach (var vent in vents) {
   total += 1;
   cur += vent.GetOxygenLevel();
  }
  if (total == 0) {
   throw new Exception("No airvents found");
  }
  return cur / total;
 }
 protected bool canPressurize(List<IMyAirVent> vents) {
  foreach (var vent in vents) {
   return vent.CanPressurize;
  }
  throw new Exception("No airvents found");
 }
}

public class JAMS_Double_Airlock : JAMS_Group {
 public override bool isValid() {
  if (vents.Count == 0) {
   return false;
  }
  foreach (var vent in vents) {
   if (!blockExists(vent) || slimBlock(vent) == null) {
    return false;
   }
  }
  if (doors.Count != 2) {
   return false;
  }
  foreach (var door in doors) {
   if (!blockExists(door) || slimBlock(door) == null) {
    return false;
   }
  }
  if (sensors.Count != 2) {
   return false;
  }
  foreach (var sensor in sensors) {
   if (!blockExists(sensor) || slimBlock(sensor) == null) {
    return false;
   }
  }
  // we do not depend on lights, so if they're dead, just remove them from the
  // list and move on with our lives
  for (int i = lights.Count - 1; i >= 0; i--) {
   var light = lights[i];
   if (!blockExists(light) || slimBlock(light) == null) {
    lights.RemoveAt(i);
   }
  }
  return true;
 }
 public override string ToString() {
  return String.Format("Double#{0}#{1}#{2}#{3}#{4:0.00}#{5}#{6}", name,
                       outer_sensor_idx, sensor_idx, (int) step_id,
                       last_pressure, is_finished, elapsed);
 }
 public override bool updateFromString(string s) {
  string[] strs = s.Split('#');
  if (strs.Length != 8) {
   // backwards compatibility
   strs = s.Split(':');
   if (strs.Length != 3) {
    return false;
   }
   if (strs[0] != "Double") {
    return false;
   }
   if (strs[1] != name) {
    return false;
   }
   int outer;
   if (Int32.TryParse(strs[2], out outer) && outer >= -1 && outer <= 1) {
    outer_sensor_idx = outer;
    return true;
   }
   return false;
  }
  if (strs[0] != "Double") {
   return false;
  }
  if (strs[1] != name) {
   return false;
  }
  int tmp_outer;
  if (!Int32.TryParse(strs[2], out tmp_outer) || tmp_outer < -1 || tmp_outer > 1) {
   throw new Exception("Wrong outer sensor idx");
  }
  int tmp_sensor;
  if (!Int32.TryParse(strs[3], out tmp_sensor) || tmp_sensor < -1 || tmp_sensor > 1) {
   throw new Exception("Wrong sensor idx");
  }
  int tmp_state;
  if (!Int32.TryParse(strs[4], out tmp_state) || tmp_state < 0 ||
      tmp_state > (int) State.STEP_DOOR_OVERRIDE) {
   throw new Exception("Wrong state");
  }
  float tmp_pressure;
  if (!Single.TryParse(strs[5], out tmp_pressure) || tmp_pressure < -1 ||
      tmp_pressure > 1) {
   throw new Exception("Wrong last pressure");
  }
  bool tmp_finished;
  if (!Boolean.TryParse(strs[6], out tmp_finished)) {
   throw new Exception("Wrong finished");
  }
  TimeSpan tmp_elapsed;
  if (!TimeSpan.TryParse(strs[7], out tmp_elapsed)) {
   throw new Exception("Wrong elapsed");
  }
  outer_sensor_idx = tmp_outer;
  sensor_idx = tmp_sensor;
  step_id = (State) tmp_state;
  last_pressure = tmp_pressure;
  is_finished = tmp_finished;
  elapsed = tmp_elapsed;
  return true;
 }
 private void mapSensorsToDoors() {
   // figure out which sensor likely belongs to which door
   int min_idx = -1;
   double min_dist = 0;
   var door = doors[0];
   for (int i = 0; i < 2; i++) {
    var d = Vector3D.Distance(door.GetPosition(), sensors[i].GetPosition());
    if (d < min_dist || min_idx == -1) {
     min_dist = d;
     min_idx = i;
    }
   }
   sensor_to_door_idx[min_idx] = 0;
   sensor_to_door_idx[(min_idx + 1) % 2] = 1;
 }

 public JAMS_Double_Airlock(Program p_in, string name_in, List<IMyAirVent> vents_in,
                     List < IMyDoor > doors_in, List < IMyLightingBlock > lights_in,
                     List < IMySensorBlock > sensors_in) {
  // in the interest of saving cycles, constructor assumes the data is valid.
  // if it isn't, it's your fault.
  sensor_to_door_idx = new Dictionary<int, int>();
  p = p_in;
  name = name_in;
  vents = vents_in;
  doors = doors_in;
  sensors = sensors_in;
  lights = lights_in;
  outer_sensor_idx = -1;

  mapSensorsToDoors();

  reset();
 }

 public override void updateFromGroup(IMyBlockGroup g) {
  var tmp_doors = new List<IMyDoor>();
  var tmp_sensors = new List<IMySensorBlock>();
  var tmp_lights = new List<IMyLightingBlock>();
  var tmp_vents = new List<IMyAirVent>();

  p.parseDoubleAirlock(g, tmp_vents, tmp_doors, tmp_lights, tmp_sensors);

  doors = tmp_doors;
  lights = tmp_lights;
  sensors = tmp_sensors;
  vents = tmp_vents;

  mapSensorsToDoors();
 }

 public override bool finished() {
  return is_finished;
 }

 public override bool tryActivate() {
  // check if any doors are active
  for (int d_idx = 0; d_idx < doors.Count; d_idx++) {
   var door = doors[d_idx];
   if (door.Open) {
    // activate the override state
    elapsed = TimeSpan.Zero;
    step_id = State.STEP_DOOR_OVERRIDE;
    last_pressure = curOxygenLevel(vents);

    setColor(lights, Color.Yellow);
    is_finished = false;
    return true;
   }
  }
  for (int s_idx = 0; s_idx < sensors.Count; s_idx++) {
   var sensor = sensors[s_idx];
   if (sensor.IsActive) {
    // activate the airlock
    elapsed = TimeSpan.Zero;
    step_id = State.STEP_INIT;
    sensor_idx = s_idx;
    last_pressure = curOxygenLevel(vents);

    setColor(lights, Color.Yellow);
    is_finished = false;
    return true;
   }
  }
  return false;
 }

 public override void reset() {
  close(doors[0]);
  close(doors[1]);
  pressurize(vents);
  turnOffLights(lights);
  sensor_idx = -1;
  last_pressure = -1;
  is_finished = true;
 }

 protected override bool advanceStateImpl() {
  // timeout
  if (elapsed.Seconds > 10) {
   setColor(lights, Color.Red);
   is_finished = true;
   goto False;
  }

  // decide what to do
  switch (step_id) {
   case State.STEP_INIT:
   {
    var sensor = sensors[sensor_idx];
    bool ready = false;
    bool stuck = false;

    bool pressureSet = false;

    if (sensor_idx == outer_sensor_idx || outer_sensor_idx == -1) {
     depressurize(vents);

     if (curOxygenLevel(vents) < 0.01) {
      pressureSet = true;
     }
    } else {
     pressurize(vents);

     if (curOxygenLevel(vents) > 0.9) {
      pressureSet = true;
     }
    }

    // if the vent is already (de)pressurizing, wait until it's fully
    // (de)pressurized, or just go to next stage if it's stuck
    stuck = (elapsed.Seconds > 5 &&
      Math.Abs(curOxygenLevel(vents) - last_pressure) < 0.05);
    if (pressureSet || stuck) {
     ready = true;
    }

    // if we're ready, open the door and proceed to next state
    if (ready && sensor.IsActive) {
     var door_idx = sensor_to_door_idx[sensor_idx];
     var door = doors[door_idx];
     // make sure we do open the door
     open(door);
     nextState();
     if (stuck) {
      setColor(lights, Color.Red);
     } else {
      setColor(lights, Color.Green);
     }
     goto True;
    }
    goto False;
   }
   case State.STEP_DOOR_IN_WAIT:
   {
    // wait 1 second after sensor stops being active, then close the
    // door and start pressurizing/depressurizing

    var sensor = sensors[sensor_idx];
    var door_idx = sensor_to_door_idx[sensor_idx];
    var door = doors[door_idx];

    if (door.OpenRatio != 0) {
     // if vent is depressurized, it's outer door
     if (!canPressurize(vents)) {
      depressurize(vents);
      outer_sensor_idx = sensor_idx;
     } else {
      pressurize(vents);
      outer_sensor_idx = (sensor_idx + 1) % 2;
     }
    }

    if (door.OpenRatio == 1 && !sensor.IsActive && elapsed.Seconds > 1) {
     close(door);
     nextState();
     goto True;
    }
    goto False;
   }
   case State.STEP_DOOR_IN:
   {
    var sensor = sensors[sensor_idx];
    var door_idx = sensor_to_door_idx[sensor_idx];
    var door = doors[door_idx];

    // close the door
    if (door.OpenRatio == 0) {
     // if it was an outer door, pressurize
     if (sensor_idx == outer_sensor_idx) {
      pressurize(vents);
     } else {
      depressurize(vents);
     }
     // update sensor id
     sensor_idx = (sensor_idx + 1) % 2;
     nextState();

     setColor(lights, Color.Yellow);
     goto True;
    } else {
     close(door);
    }
    goto False;
   }
   case State.STEP_DOOR_OUT:
   {
    // wait until the room is fully pressurized/depressurized
    bool pressureSet = false;
    bool stuck = false;
    if (sensor_idx == outer_sensor_idx && curOxygenLevel(vents) < 0.01) {
     pressureSet = true;
    } else if (sensor_idx != outer_sensor_idx && curOxygenLevel(vents) > 0.9) {
     pressureSet = true;
    }
    stuck = (elapsed.Seconds > 5 &&
      Math.Abs(curOxygenLevel(vents) - last_pressure) < 0.05);
    if (pressureSet || stuck) {
     var door_idx = sensor_to_door_idx[sensor_idx];
     var door = doors[door_idx];
     // open the door
     open(door);
     nextState();
     if (stuck) {
      setColor(lights, Color.Red);
     } else {
      setColor(lights, Color.Green);
     }
     goto True;
    }
    goto False;
   }
   case State.STEP_DOOR_OUT_WAIT:
   {
    var door_idx = sensor_to_door_idx[sensor_idx];
    var door = doors[door_idx];

    var sensor = sensors[sensor_idx];
    if (door.OpenRatio == 1 || sensor.IsActive) {
     nextState();
     goto True;
    }
    goto False;
   }
   case State.STEP_DOOR_CLOSE_WAIT:
   {
    var sensor = sensors[sensor_idx];
    if (!sensor.IsActive && elapsed.Seconds > 1) {
     nextState();
    }
    goto False;
   }
   case State.STEP_DOOR_CLOSE:
   {
    var door_idx = sensor_to_door_idx[sensor_idx];
    var door = doors[door_idx];

    if (door.OpenRatio != 0) {
     setColor(lights, Color.Green);
     close(door);
    } else {
     is_finished = true;
    }
    goto False;
   }
   case State.STEP_DOOR_OVERRIDE: {
    // find the open door
    bool hasOpenDoors = false;
    for (int door_idx = 0; door_idx < doors.Count; door_idx++) {
     var door = doors[door_idx];
     if (elapsed.Seconds > 2) {
      if (door.OpenRatio != 0) {
       setColor(lights, Color.Green);
       close(door);
       hasOpenDoors = true;
      }
     } else {
      // don't die until we've tried to close the doors
      hasOpenDoors = true;
     }
    }
    if (!hasOpenDoors) {
     is_finished = true;
    }
    goto False;
   }
  }
  // all hail the mighty raptor!
 True:
  last_pressure = curOxygenLevel(vents);
  return true;
 False:
  last_pressure = curOxygenLevel(vents);
  return false;
 }

 private void nextState() {
  step_id++;
  elapsed = TimeSpan.Zero;
 }

 enum State {
  STEP_INIT,
  STEP_DOOR_IN_WAIT,
  STEP_DOOR_IN,
  STEP_DOOR_OUT,
  STEP_DOOR_OUT_WAIT,
  STEP_DOOR_CLOSE_WAIT,
  STEP_DOOR_CLOSE,
  STEP_DOOR_OVERRIDE
 };

 private List<IMyDoor> doors;
 private List<IMySensorBlock> sensors;
 private List<IMyLightingBlock> lights;
 private List<IMyAirVent> vents;
 private Dictionary<int,int> sensor_to_door_idx; // maps sensor idx to door idx
 private int outer_sensor_idx; // -1 means we have no idea
 private State step_id;
 private int sensor_idx;
 private float last_pressure;
 private bool is_finished;
}

public class JAMS_Single_Airlock : JAMS_Group {
 public override bool isValid() {
  if (vents.Count == 0) {
   return false;
  }
  foreach (var vent in vents) {
   if (!blockExists(vent) || slimBlock(vent) == null) {
    return false;
   }
  }
  if (doors.Count == 0) {
   return false;
  }
  foreach (var door in doors) {
   if (!blockExists(door) || slimBlock(door) == null) {
    return false;
   }
  }
  if (sensors.Count == 0) {
   return false;
  }
  foreach (var sensor in sensors) {
   if (!blockExists(sensor) || slimBlock(sensor) == null) {
    return false;
   }
  }
  // we do not depend on lights, so if they're dead, just remove them from the
  // list and move on with our lives
  for (int i = lights.Count - 1; i >= 0; i--) {
   var light = lights[i];
   if (!blockExists(light) || slimBlock(light) == null) {
    lights.RemoveAt(i);
   }
  }
  return true;
 }
 public override string ToString() {
  var sensor_idx = active_sensor == null ? -1 : sensors.IndexOf(active_sensor);
  return String.Format("Single#{0}#{1}#{2}#{3:0.00}#{4}#{5}", name,
                       sensor_idx, (int) step_id,
                       last_pressure, is_finished, elapsed);
 }
 public override bool updateFromString(string s) {
  string[] strs = s.Split('#');
  if (strs.Length != 7) {
   return false;
  }
  if (strs[0] != "Single") {
   return false;
  }
  if (strs[1] != name) {
   return false;
  }
  int tmp_sensor;
  if (!Int32.TryParse(strs[2], out tmp_sensor) || tmp_sensor < -1 || tmp_sensor > 1) {
   throw new Exception("Wrong sensor idx");
  }
  int tmp_state;
  if (!Int32.TryParse(strs[3], out tmp_state) || tmp_state < 0 ||
      tmp_state > (int) State.STEP_PRESSURIZE) {
   throw new Exception("Wrong state");
  }
  float tmp_pressure;
  if (!Single.TryParse(strs[4], out tmp_pressure) || tmp_pressure < -1 ||
      tmp_state > 1) {
   throw new Exception("Wrong last pressure");
  }
  bool tmp_finished;
  if (!Boolean.TryParse(strs[5], out tmp_finished)) {
   throw new Exception("Wrong finished");
  }
  TimeSpan tmp_elapsed;
  if (!TimeSpan.TryParse(strs[6], out tmp_elapsed)) {
   throw new Exception("Wrong elapsed");
  }
  if (tmp_sensor != -1) {
   active_sensor = sensors[tmp_sensor];
  }
  step_id = (State) tmp_state;
  last_pressure = tmp_pressure;
  is_finished = tmp_finished;
  elapsed = tmp_elapsed;
  return true;
 }

 public JAMS_Single_Airlock(Program p_in, string name_in, List<IMyAirVent> vents_in,
                            List < IMyDoor > doors_in, List < IMyLightingBlock > lights_in,
                            List < IMySensorBlock > sensors_in) {
  p = p_in;
  name = name_in;
  vents = vents_in;
  doors = doors_in;
  sensors = sensors_in;
  lights = lights_in;

  reset();
 }

 public override void updateFromGroup(IMyBlockGroup g) {
  var tmp_doors = new List<IMyDoor>();
  var tmp_sensors = new List<IMySensorBlock>();
  var tmp_lights = new List<IMyLightingBlock>();
  var tmp_vents = new List<IMyAirVent>();

  p.parseSingleAirlock(g, tmp_vents, tmp_doors, tmp_lights, tmp_sensors);

  doors = tmp_doors;
  lights = tmp_lights;
  sensors = tmp_sensors;
  vents = tmp_vents;
 }

 public override bool finished() {
  return is_finished;
 }

 public override bool tryActivate() {
  // check if any doors are open
  for (int d_idx = 0; d_idx < doors.Count; d_idx++) {
   var door = doors[d_idx];
   if (door.Open) {
    // wait for hangar to clear and close the doors
    elapsed = TimeSpan.Zero;
    step_id = State.STEP_WAIT_FOR_EXIT;
    last_pressure = curOxygenLevel(vents);

    setColor(lights, Color.Yellow);
    is_finished = false;
    return true;
   }
  }
  var sensor = getActiveSensor();
  if (sensor != null) {
   // activate the airlock
   elapsed = TimeSpan.Zero;
   step_id = State.STEP_DEPRESSURIZE;
   active_sensor = sensor;
   last_pressure = curOxygenLevel(vents);
   setColor(lights, Color.Yellow);
   is_finished = false;
   return true;
  }
  return false;
 }

 public override void reset() {
  foreach (var door in doors) {
   close(door);
  }
  pressurize(vents);
  turnOffLights(lights);
  active_sensor = null;
  last_pressure = -1;
  is_finished = true;
 }

 protected override bool advanceStateImpl() {
  // decide what to do
  switch (step_id) {
   case State.STEP_DEPRESSURIZE:
   {
    bool ready = false;
    bool stuck = false;

    bool pressureSet = false;

    // if our sensor is no longer active, cancel
    if (!active_sensor.IsActive) {
     is_finished = true;
     return false;
    }

    depressurize(vents);

    if (curOxygenLevel(vents) < 0.01) {
     pressureSet = true;
    }

    // if the vent is already depressurizing, wait until it's fully
    // depressurized, or just go to next stage if it's stuck
    stuck = (elapsed.Seconds > 15 &&
      Math.Abs(curOxygenLevel(vents) - last_pressure) < 0.05);
    if (pressureSet || stuck) {
     ready = true;
    }

    // if we're ready, open the door and proceed to next state
    if (ready) {
     // make sure we do open the door
     foreach (var door in doors) {
      open(door);
     }
     nextState();
     if (stuck) {
      setColor(lights, Color.Red);
     } else {
      setColor(lights, Color.Green);
     }
     goto True;
    }
    goto False;
   }
   case State.STEP_OPEN_DOOR:
   {
    // wait until the door is fully open

    bool doors_not_fully_open = false;
    foreach (var door in doors) {
     doors_not_fully_open |= (door.OpenRatio != 1);
    }

    if (!doors_not_fully_open) {
     setColor(lights, Color.Green);
     nextState();
     goto True;
    }
    goto False;
   }
   case State.STEP_WAIT_FOR_EXIT:
   {
    // no time limit on this, as flying around the hangar is supposed to be slow
    if (getActiveSensor() == null && elapsed.Seconds > 3) {
     nextState();
     setColor(lights, Color.Yellow);
     foreach (var door in doors) {
      close(door);
     }
     goto True;
    }
    goto False;
   }
   case State.STEP_CLOSE_DOOR:
   {
    // wait until the door is fully closed

    bool doors_not_fully_closed = false;
    foreach (var door in doors) {
     doors_not_fully_closed |= (door.OpenRatio != 0);
    }

    var sensor = getActiveSensor();
    if (sensor != null) {
     active_sensor = sensor;
     step_id = State.STEP_DEPRESSURIZE;
     last_pressure = curOxygenLevel(vents);
     setColor(lights, Color.Yellow);
     goto True;
    }

    if (!doors_not_fully_closed) {
     setColor(lights, Color.Green);
     pressurize(vents);
     nextState();
     goto True;
    }
    goto False;
   }
   case State.STEP_PRESSURIZE:
   {
    // wait until the room is fully pressurized
    bool pressureSet = false;
    bool stuck = false;
    if (curOxygenLevel(vents) > 0.9) {
     pressureSet = true;
    }
    stuck = (elapsed.Seconds > 15 &&
      Math.Abs(curOxygenLevel(vents) - last_pressure) < 0.05);
    if (pressureSet || stuck) {
     if (stuck) {
      setColor(lights, Color.Red);
     } else {
      setColor(lights, Color.Green);
     }
     is_finished = true;
    }
    goto False;
   }
  }
  // all hail the mighty raptor!
 True:
  last_pressure = curOxygenLevel(vents);
  return true;
 False:
  last_pressure = curOxygenLevel(vents);
  return false;
 }

 private void nextState() {
  step_id++;
  elapsed = TimeSpan.Zero;
 }

 private IMySensorBlock getActiveSensor() {
  foreach (var sensor in sensors) {
   if (sensor.IsActive) {
    return sensor;
   }
  }
  return null;
 }

 enum State {
  STEP_DEPRESSURIZE,
  STEP_OPEN_DOOR,
  STEP_WAIT_FOR_EXIT,
  STEP_CLOSE_DOOR,
  STEP_PRESSURIZE
 };

 private List<IMyDoor> doors;
 private List<IMySensorBlock> sensors;
 private List<IMyLightingBlock> lights;
 private List<IMyAirVent> vents;
 private State step_id;
 private IMySensorBlock active_sensor;
 private float last_pressure;
 private bool is_finished;
}

public void parseAirlock(IMyBlockGroup g, List<IMyAirVent> vents,
                         List<IMyDoor> doors, List<IMyLightingBlock> lights,
                         List<IMySensorBlock> sensors) {
 var blocks = new List<IMyTerminalBlock>();
 g.GetBlocks(blocks);

 // we need find two doors and two sensors
 for (int i = 0; i < blocks.Count; i++) {
  var block = blocks[i];
  if (slimBlock(block) == null) {
   continue;
  }
  if (block is IMyDoor)
   doors.Add(block as IMyDoor);
  else if (block is IMySensorBlock)
   sensors.Add(block as IMySensorBlock);
  else if (block is IMyAirVent) {
   vents.Add(block as IMyAirVent);
  }
  else if (block is IMyLightingBlock)
   lights.Add(block as IMyLightingBlock);
  else {
   throw new Exception("Unexpected block: " + block.CustomName);
  }
 }
}

public void parseDoubleAirlock(IMyBlockGroup g, List<IMyAirVent> vents,
                               List<IMyDoor> doors, List<IMyLightingBlock> lights,
                               List<IMySensorBlock> sensors) {
 parseAirlock(g, vents, doors, lights, sensors);
 if (doors.Count != 2) {
  throw new Exception("Need to have two doors");
 }
 if (sensors.Count != 2) {
  throw new Exception("Need to have two sensors");
 }
 if (vents.Count == 0) {
  throw new Exception("No air vents found");
 }
}

public void parseSingleAirlock(IMyBlockGroup g, List<IMyAirVent> vents,
                               List<IMyDoor> doors, List<IMyLightingBlock> lights,
                               List<IMySensorBlock> sensors) {
 parseAirlock(g, vents, doors, lights, sensors);
 if (doors.Count == 0) {
  throw new Exception("No doors found");
 }
 if (sensors.Count == 0) {
  throw new Exception("No sensors found");
 }
 if (vents.Count == 0) {
  throw new Exception("No air vents found");
 }
}

public JAMS_Group createDoubleFromGroup(IMyBlockGroup g, Program p) {
 var vents = new List<IMyAirVent>();
 var doors = new List<IMyDoor>();
 var lights = new List<IMyLightingBlock>();
 var sensors = new List<IMySensorBlock>();
 parseDoubleAirlock(g, vents, doors, lights, sensors);
 return new JAMS_Double_Airlock(p, g.Name, vents, doors, lights, sensors);
}

public JAMS_Group createSingleFromGroup(IMyBlockGroup g, Program p) {
 var vents = new List<IMyAirVent>();
 var doors = new List<IMyDoor>();
 var lights = new List<IMyLightingBlock>();
 var sensors = new List<IMySensorBlock>();
 parseSingleAirlock(g, vents, doors, lights, sensors);
 return new JAMS_Single_Airlock(p, g.Name, vents, doors, lights, sensors);
}

JAMS_Group createFromGroup(IMyBlockGroup g) {
 try {
  return createDoubleFromGroup(g, this);
 } catch (Exception) {
 }
 try {
  return createSingleFromGroup(g, this);
 } catch (Exception) {
 }
 return null;
}

/*
 * Grid stuff
 */

IMySlimBlock slimBlock(IMyTerminalBlock b) {
 return b.CubeGrid.GetCubeBlock(b.Position);
}

bool blockExists(IMyTerminalBlock b) {
 return b.CubeGrid.CubeExists(b.Position);
}

// find which grid has a block at world_pos, excluding "self"
IMyCubeGrid findGrid(Vector3D w_p, IMyCubeGrid self, List < IMyCubeGrid > grids) {
 foreach (var g in grids) {
  if (g == self) {
   continue;
  }
  var pos = g.WorldToGridInteger(w_p);
  if (g.CubeExists(pos)) {
   return g;
  }
 }
 return null;
}

IMyCubeGrid getConnectedGrid(IMyShipConnector c) {
 if (!c.IsConnected) {
  return null;
 }
 // skip connectors connecting to the same grid
 var o = c.OtherConnector;
 if (o.CubeGrid == c.CubeGrid) {
  return null;
 }
 return o.CubeGrid;
}

IMyCubeGrid getConnectedGrid(IMyMotorBase r, List < IMyCubeGrid > grids) {
 if (!r.IsAttached) {
  return null;
 }
 var pos = r.Position;
 var or = r.Orientation;
 var dir = new Vector3I(0, 1, 0);
 Matrix m;
 or.GetMatrix(out m);
 Vector3I.Transform(ref dir, ref m, out dir);
 var w_p = r.CubeGrid.GridIntegerToWorld(pos + dir);
 return findGrid(w_p, r.CubeGrid, grids);
}

IMyCubeGrid getConnectedGrid(IMyPistonBase p, List < IMyCubeGrid > grids) {
 if (!p.IsAttached) {
  return null;
 }
 var pos = p.Position;
 var or = p.Orientation;
 bool is_large = p.BlockDefinition.ToString().Contains("Large");
 var up = (int) Math.Round(p.CurrentPosition / (is_large ? 2.5 : 0.5));
 var dir = new Vector3I(0, 2 + up, 0);
 Matrix m;
 or.GetMatrix(out m);
 Vector3I.Transform(ref dir, ref m, out dir);
 var w_p = p.CubeGrid.GridIntegerToWorld(pos + dir);
 return findGrid(w_p, p.CubeGrid, grids);
}

// getting local grids is not trivial, we're basically building a graph of all
// grids and figure out which ones are local to us. we are also populating
// object lists in the meantime
List < IMyCubeGrid > getLocalGrids(bool force_update = false) {
 if (local_grids != null && !force_update) {
  return local_grids;
 }

 // piston and rotor lists are local, we don't need them once we're done
 var blocks = new List<IMyTerminalBlock>();
 var pistons = new List < IMyTerminalBlock > ();
 var rotors = new List < IMyTerminalBlock > ();
 var connectors = new List < IMyTerminalBlock > ();
 var grids_set = new HashSet<IMyCubeGrid>();

 // get all blocks that are accessible to GTS
 GridTerminalSystem.GetBlocks(blocks);

 // for each block, get its grid, store data for this grid, and populate respective
 // object list if it's one of the objects we're interested in
 foreach (var b in blocks) {
  if (slimBlock(b) == null) {
   continue;
  }
  grids_set.Add(b.CubeGrid);

  // fill all lists
  if (b is IMyShipConnector) {
   connectors.Add(b);
  } else if (b is IMyPistonBase) {
   pistons.Add(b);
  } else if (b is IMyMotorBase) {
   rotors.Add(b);
  }
 }
 // free the memory!
 blocks.Clear();
 var grids = new List<IMyCubeGrid>(grids_set);

 // now, build a graph of all grids
 var gr = new Graph<IMyCubeGrid>();

 // first, go through all pistons
 foreach (IMyPistonBase p in pistons) {
  var connected_grid = getConnectedGrid(p, grids);

  if (connected_grid != null) {
   // grids connected to pistons are local to their source
   gr.addEdge(p.CubeGrid, connected_grid, false);
  }
 }

 // do the same for rotors
 foreach (IMyMotorBase rotor in rotors) {
  var connected_grid = getConnectedGrid(rotor, grids);

  if (connected_grid != null) {
   // grids connected to locals are local to their source
   gr.addEdge(rotor.CubeGrid, connected_grid, false);
  }
 }

 // do the same for connectors
 foreach (IMyShipConnector c in connectors) {
  var connected_grid = getConnectedGrid(c);

  if (connected_grid != null) {
   // grids connected to connectors belong to a different ship
   gr.addEdge(c.CubeGrid, connected_grid, true);
  }
 }

 // make sure we remove all unnecessary edges from the graph
 gr.validateGraph();

 // now, get our actual local grid
 local_grids = gr.getGridRegion(Me.CubeGrid);

 return local_grids;
}

void showOnHud(IMyTerminalBlock b) {
 if (b.GetProperty("ShowOnHUD") != null) {
  b.SetValue("ShowOnHUD", true);
 }
}

void hideFromHud(IMyTerminalBlock b) {
 if (b.GetProperty("ShowOnHUD") != null) {
  b.SetValue("ShowOnHUD", false);
 }
}

public void filterLocalGrid(List < IMyTerminalBlock > blocks) {
 var grids = getLocalGrids();
 for (int i = blocks.Count - 1; i >= 0; i--) {
  var block = blocks[i];
  var grid = block.CubeGrid;
  if (!grids.Contains(grid)) {
   blocks.RemoveAt(i);
  }
 }
}

void saveAirlocks() {
 StringBuilder sb = new StringBuilder();
 for (int i = 0; i < airlocks.Count; i++) {
  sb.Append(airlocks[i].ToString());
  if (i != airlocks.Count - 1)
   sb.Append(";");
 }
 Storage = sb.ToString();
}

void updateAirlocks() {
 string[] group_strs = Storage.Split(';');
 var skipList = new List<int>();

 bool hasInvalidInfo = false;

 for (int i = airlocks.Count - 1; i >= 0; i--) {
  var airlock = airlocks[i];

  for (int j = 0; j < group_strs.Length; j++) {
   if (skipList.Contains(j)) {
    continue;
   }
   try {
    if (airlock.updateFromString(group_strs[j])) {
     if (!airlock.finished()) {
      active_airlocks.Add(airlock);
      throw new Exception();
     }
     skipList.Add(j);
    }
   } catch (Exception) {
    // silently ignore errors
    hasInvalidInfo = true;
   }
  }
 }
 // if there's some invalid info in Storage, overwrite it
 if (hasInvalidInfo) {
  saveAirlocks();
 }
}

void pressurize(IMyAirVent av) {
 av.ApplyAction("Depressurize_Off");
 av.ApplyAction("Depressurize_On");
 av.ApplyAction("Depressurize_Off");
}

void depressurize(IMyAirVent av) {
 av.ApplyAction("Depressurize_On");
 av.ApplyAction("Depressurize_Off");
 av.ApplyAction("Depressurize_On");
}

void open(IMyDoor door) {
 door.ApplyAction("OnOff_On");
 door.ApplyAction("Open_On");
 door.ApplyAction("Open_Off");
 door.ApplyAction("Open_On");
}

void close(IMyDoor door) {
 door.ApplyAction("OnOff_On");
 door.ApplyAction("Open_Off");
 door.ApplyAction("Open_On");
 door.ApplyAction("Open_Off");
}

void setColor(List<IMyLightingBlock> lights, Color c) {
 for (int i = 0; i < lights.Count; i++) {
  var light = lights[i];
  if (light.GetValue < Color > ("Color").Equals(c) && light.Enabled) {
   continue;
  }
  light.SetValue("Color", c);
  // make sure we switch the color of the texture as well
  light.ApplyAction("OnOff_Off");
  light.ApplyAction("OnOff_On");
 }
}

void turnOffLights(List<IMyLightingBlock> lights) {
 for (int i = 0; i < lights.Count; i++) {
  var light = lights[i];
  light.ApplyAction("OnOff_Off");
 }
}

void validateAirlocks() {
 for (int i = active_airlocks.Count - 1; i >= 0; i--) {
  var airlock = active_airlocks[i];
  if (!airlock.isValid()) {
   // remove this airlock from existence
   active_airlocks.RemoveAt(i);
  }
 }
  for (int i = airlocks.Count - 1; i >= 0; i--) {
   var airlock = airlocks[i];
   if (!airlock.isValid()) {
    // remove this airlock from existence
    airlocks.RemoveAt(i);
   }
  }
}

void s_refreshAirlocks() {
 // don't refresh anything until we're idle, otherwise things get nasty
 if (active_airlocks.Count != 0) {
  return;
 }
 var tmp_list = new List<JAMS_Group>();
 var name_to_airlock = new Dictionary<string, JAMS_Group>();
 foreach (var airlock in airlocks) {
  name_to_airlock.Add(airlock.name, airlock);
 }

 var groups = new List<IMyBlockGroup>();
 GridTerminalSystem.GetBlockGroups(groups);
 for (int i = 0; i < groups.Count; i++) {
  var group = groups[i];
  // skip groups we don't want
  if (!group.Name.StartsWith("JAMS")) {
   continue;
  }
  var blocks = new List<IMyTerminalBlock>();
  group.GetBlocks(blocks);
  filterLocalGrid(blocks);
  if (blocks.Count == 0) {
   // this group is from a foreign grid
   continue;
  }
  if (!name_to_airlock.ContainsKey(group.Name)) {
   // just add this group to list
   var airlock = createFromGroup(group);
   if (airlock != null) {
    tmp_list.Add(airlock);
   } else {
    Echo("Error parsing group " + group.Name);
   }
  } else {
   // find old airlock and update it
   var airlock = name_to_airlock[group.Name];
   try {
    airlock.p = this; // update pointer to program
    airlock.updateFromGroup(group);
    tmp_list.Add(airlock);
   } catch (Exception) {
    Echo("Error parsing group " + group.Name);
   }
  }
 }
 airlocks = tmp_list;
}

void s_checkAirlocks() {
 foreach (var airlock in airlocks) {
  if (active_airlocks.Contains(airlock)) {
   continue;
  }
  if (airlock.tryActivate()) {
   active_airlocks.Add(airlock);
  } else {
   airlock.reset();
  }
 }
}

void s_processAirlocks() {
 var death_row = new List<int>();

 // process all airlocks that are currently active
 for (int i = active_airlocks.Count - 1; i >= 0; i--) {
  var airlock = active_airlocks[i];

  while (airlock.advanceState()) {}

  if (airlock.finished()) {
   airlock.reset();
   death_row.Add(i);
  }
 }
 foreach (var i in death_row) {
  active_airlocks.RemoveAt(i);
 }
}

void s_refreshGrids() {
 getLocalGrids(true);
}

int[] state_cycle_counts;
int cycle_count;

bool canContinue() {
 bool hasHeadroom = false;
 bool isFirstRun = state_cycle_counts[current_state] == 0;
 var prev_state = current_state == 0 ? states.Length - 1 : current_state - 1;
 var next_state = (current_state + 1) % states.Length;
 var cur_i = Runtime.CurrentInstructionCount;

 // now store how many cycles we've used during this iteration
 state_cycle_counts[current_state] = cur_i - cycle_count;

 var last_cycle_count = state_cycle_counts[next_state];

 // if we have enough headroom (we want no more than 80% cycle/method count)
 int projected_cycle_count = cur_i + last_cycle_count;
 Decimal cycle_percentage = (Decimal) projected_cycle_count / Runtime.MaxInstructionCount;

 // to speed up initial run, keep 40% headroom for next states
 bool initRunCycleHeadroom = isFirstRun && cycle_percentage <= 0.4M;

 bool runCycleHeadroom = !isFirstRun && cycle_percentage <= 0.8M;

 if (initRunCycleHeadroom || runCycleHeadroom) {
  hasHeadroom = true;
 }

 // advance current state and store IL count values
 current_state = next_state;
 cycle_count = cur_i;

 return hasHeadroom;
}

void ILReport(int states_executed) {
 string il_str = String.Format("IL Count: {0}/{1} ({2:0.0}%)",
    Runtime.CurrentInstructionCount,
    Runtime.MaxInstructionCount,
    (Decimal) Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount * 100M);
 Echo(String.Format("States executed: {0}", states_executed));
 Echo(il_str);
}

public void Save() {
 saveAirlocks();
}

public Program() {
 states = new Action [] {
  s_refreshGrids,
  s_refreshAirlocks,
  s_checkAirlocks,
  s_processAirlocks,
 };
 Me.SetCustomName("JAMS CPU");
 hideFromHud(Me);

 // do an out of cycle refresh, because we need to find airlocks that saved to Storage
 s_refreshGrids();
 s_refreshAirlocks();
 updateAirlocks();

 // make sure we start from state 2, as first two states we already completed
 current_state = 2;
 state_cycle_counts = new int[states.Length];
}

void Main() {
 Echo(String.Format("JAMS version {0}", VERSION));
 int num_states = 0;
 cycle_count = 0;
 validateAirlocks();
 do {
  try {
   states[current_state]();
  } catch (Exception e) {
   Me.SetCustomName("JAMS Exception");
   showOnHud(Me);
   Echo(e.StackTrace);
   throw;
  }
  num_states++;
 } while (canContinue() && num_states < states.Length);

 Echo(String.Format("Airlocks count: {0}", airlocks.Count));
 ILReport(num_states);
}
