/*
 * JAMS v1.1alpha1
 *
 * (JAMS Airlock Management System)
 *
 * Published under "do whatever you want with it" license (aka public domain).
 *
 */

public abstract class JAMS_Group {
 public abstract void updateFromGroup(IMyBlockGroup g);
 public abstract void updateFromString(string s);
 public abstract bool tryActivate(); // check if group should be activated
 public abstract bool finished(); // check if state can be retired
 protected abstract bool advanceStateImpl(); // move between states
 public abstract void reset(); // reset the airlock
 public abstract string toString(); // return a string representation of this group
 public string name; // name of the group
 public Program p; // reference to our programmable block
 protected TimeSpan elapsed; // timestamp when the state has started
 public bool advanceState() {
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
 protected void pressurize(IMyAirVent vent) {
  if (p == null) {
   return;
  }
  p.pressurize(vent);
 }
 protected void depressurize(IMyAirVent vent) {
  if (p == null) {
   return;
  }
  p.depressurize(vent);
 }
 protected void turnOffLights(List<IMyLightingBlock> lights) {
  if (p == null) {
   return;
  }
  p.turnOffLights(lights);
 }
}

public class JAMS_Airlock : JAMS_Group {
 public override string toString() {
  return String.Format("Airlock:{0}:{1}", name, outer_sensor_idx);
 }
 public override void updateFromString(string s) {
  string[] strs = s.Split(':');
  // if this isn't our type of airlock, bail out
  if (strs[0] != "Airlock") {
   throw new Exception("Wrong airlock type");
  }
  if (name != strs[1]) {
   throw new Exception("Wrong group name");
  }
  outer_sensor_idx = Convert.ToInt32(strs[2]);
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

 public JAMS_Airlock(Program p_in, string name_in, IMyAirVent v,
                     List < IMyDoor > doors_in, List < IMyLightingBlock > lights_in,
                     List < IMySensorBlock > sensors_in, int outer) {
  if (name_in == null) {
   throw new Exception("Name is null");
  }
  if (v == null) {
   throw new Exception("Airvent is null");
  }
  if (doors_in == null || doors_in.Count != 2) {
   throw new Exception ("Doors are null");
  }
  foreach (var door in doors_in) {
   if (door == null) {
    throw new Exception("Door is null");
   }
  }
  if (sensors_in == null || sensors_in.Count != 2) {
   throw new Exception ("Sensors are null");
  }
  foreach (var sensor in sensors_in) {
   if (sensor == null) {
    throw new Exception("Sensor is null");
   }
  }
  if (lights_in == null) {
   throw new Exception ("Lights are null");
  }
  foreach (var light in lights_in) {
   if (light == null) {
    throw new Exception("Light is null");
   }
  }
  if (outer < -1 || outer > 1) {
   throw new Exception("Wrong value for outer idx");
  }
  sensor_to_door_idx = new Dictionary<int, int>();
  p = p_in;
  name = name_in;
  vent = v;
  doors = doors_in;
  sensors = sensors_in;
  lights = lights_in;
  outer_sensor_idx = outer;

  reset();
 }

 public override void updateFromGroup(IMyBlockGroup g) {
  var blocks = new List<IMyTerminalBlock>();
  var tmp_doors = new List<IMyDoor>();
  var tmp_sensors = new List<IMySensorBlock>();
  var tmp_lights = new List<IMyLightingBlock>();
  IMyAirVent tmp_vent = null;
  g.GetBlocks(blocks);

  // we need find two doors and two sensors
  for (int i = 0; i < blocks.Count; i++) {
   var block = blocks[i];
   if ((block as IMyDoor) != null)
    tmp_doors.Add(block as IMyDoor);
   else if ((block as IMySensorBlock) != null)
    tmp_sensors.Add(block as IMySensorBlock);
   else if ((block as IMyAirVent) != null) {
    if (tmp_vent != null) {
     throw new Exception("Need to have one airvent");
    }
    tmp_vent = (block as IMyAirVent);
   }
   else if ((block as IMyLightingBlock) != null)
    tmp_lights.Add(block as IMyLightingBlock);
   else {
    throw new Exception("Unexpected block: " + block.CustomName);
   }
  }
  if (tmp_doors.Count != 2) {
   throw new Exception("Need to have two doors");
  }
  if (tmp_sensors.Count != 2) {
   throw new Exception("Need to have two sensors");
  }
  if (tmp_vent == null) {
   throw new Exception("No air vent found");
  }
  doors = tmp_doors;
  lights = tmp_lights;
  sensors = tmp_sensors;
  vent = tmp_vent;

  mapSensorsToDoors();
 }

 public static JAMS_Group createFromGroup(IMyBlockGroup g, Program p) {
  if (g == null) {
   return null;
  }
  var blocks = new List<IMyTerminalBlock>();
  var tmp_doors = new List<IMyDoor>();
  var tmp_sensors = new List<IMySensorBlock>();
  var tmp_lights = new List<IMyLightingBlock>();
  IMyAirVent tmp_vent = null;
  g.GetBlocks(blocks);

  // we need find two doors and two sensors
  for (int i = 0; i < blocks.Count; i++) {
   var block = blocks[i];
   if ((block as IMyDoor) != null)
    tmp_doors.Add(block as IMyDoor);
   else if ((block as IMySensorBlock) != null)
    tmp_sensors.Add(block as IMySensorBlock);
   else if ((block as IMyAirVent) != null) {
    if (tmp_vent != null) {
     throw new Exception("Need to have one airvent");
    }
    tmp_vent = (block as IMyAirVent);
   }
   else if ((block as IMyLightingBlock) != null)
    tmp_lights.Add(block as IMyLightingBlock);
   else {
    throw new Exception("Unexpected block: " + block.CustomName);
   }
  }
  if (tmp_doors.Count != 2) {
   throw new Exception("Need to have two doors");
  }
  if (tmp_sensors.Count != 2) {
   throw new Exception("Need to have two sensors");
  }
  if (tmp_vent == null) {
   throw new Exception("No air vent found");
  }

  return new JAMS_Airlock(p, g.Name, tmp_vent, tmp_doors, tmp_lights, tmp_sensors, -1);
 }

 public override bool finished() {
  return is_finished;
 }

 public override bool tryActivate() {
  for (int s_idx = 0; s_idx < sensors.Count; s_idx++) {
   var sensor = sensors[s_idx];
   if (sensor.IsActive) {
    // activate the airlock
    elapsed = TimeSpan.Zero;
    step_id = State.STEP_INIT;
    sensor_idx = s_idx;
    last_pressure = -1;

    setColor(lights, Color.Yellow);
    return true;
   }
  }
  // check if any doors are active
  for (int d_idx = 0; d_idx < doors.Count; d_idx++) {
   var door = doors[d_idx];
   if (door.Open) {
    // activate the override state
    elapsed = TimeSpan.Zero;
    step_id = State.STEP_DOOR_OVERRIDE;
    last_pressure = -1;

    setColor(lights, Color.Yellow);
    return true;
   }
  }
  return false;
 }

 public override void reset() {
  close(doors[0]);
  close(doors[1]);
  pressurize(vent);
  turnOffLights(lights);
  sensor_idx = -1;
  last_pressure = -1;
  is_finished = false;
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
    bool ready = false;
    bool stuck = false;

    bool pressureSet = false;

    if (sensor_idx == outer_sensor_idx || outer_sensor_idx == -1) {
     depressurize(vent);

     if (vent.GetOxygenLevel() == 0) {
      pressureSet = true;
     }
    } else {
     pressurize(vent);

     if (vent.GetOxygenLevel() == 100) {
      pressureSet = true;
     }
    }

    // if the vent is already (de)pressurizing, wait until it's fully
    // (de)pressurized, or just go to next stage if it's stuck
    stuck = (elapsed.Seconds > 5 &&
      (int) vent.GetOxygenLevel() == last_pressure);
    if (pressureSet || stuck) {
     ready = true;
    }

    // if we're ready, open the door and proceed to next state
    if (ready) {
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

    // if vent is depressurized, it's outer door
    if (!vent.CanPressurize) {
     outer_sensor_idx = sensor_idx;
    } else {
     outer_sensor_idx = (sensor_idx + 1) % 2;
    }

    // close the door
    if (door.OpenRatio == 0) {
     // if it was an outer door, pressurize
     if (sensor_idx == outer_sensor_idx) {
      pressurize(vent);
     } else {
      depressurize(vent);
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
    if (sensor_idx == outer_sensor_idx && vent.GetOxygenLevel() == 0) {
     pressureSet = true;
    } else if (sensor_idx != outer_sensor_idx && vent.GetOxygenLevel() == 100) {
     pressureSet = true;
    }
    stuck = (elapsed.Seconds > 5 &&
      (int) vent.GetOxygenLevel() == last_pressure);
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
  last_pressure = (int) vent.GetOxygenLevel();
  return true;
 False:
  last_pressure = (int) vent.GetOxygenLevel();
  return false;
 }

 private void nextState() {
  step_id++;
  elapsed = TimeSpan.Zero;
  last_pressure = -1;
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
 private Dictionary<int,int> sensor_to_door_idx; // maps sensor idx to door idx
 private List<IMyLightingBlock> lights;
 private IMyAirVent vent;
 private int outer_sensor_idx; // -1 means we have no idea
 private State step_id;
 private int sensor_idx;
 private int last_pressure;
 private bool is_finished;
}

JAMS_Group createFromGroup(IMyBlockGroup g) {
 try {
  return JAMS_Airlock.createFromGroup(g, this);
 } catch (Exception) {
 }
 return null;
}

public List<JAMS_Group> active_airlocks = new List<JAMS_Group>();
public List<JAMS_Group> airlocks = new List<JAMS_Group>();

// state machine
Action [] states = null;

int current_state;

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
 for (int i = blocks.Count - 1; i >= 0; i--) {
  var block = blocks[i];
  var grid = block.CubeGrid;
  if (grid != Me.CubeGrid) {
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
 if (Storage == "") {
  return;
 }
 string[] group_strs = Storage.Split(';');
 var skipList = new List<int>();

 for (int i = airlocks.Count - 1; i >= 0; i--) {
  var airlock = airlocks[i];

  for (int j = 0; j < group_strs.Length; j++) {
   if (skipList.Contains(j)) {
    continue;
   }
   try {
    airlock.updateFromString(group_strs[j]);
    skipList.Add(j);
   } catch (Exception) {}
  }
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
  }
 }
}

void s_processAirlocks() {
 var death_row = new List<int>();

 // process all airlocks that are currently active
 for (int i = 0; i < active_airlocks.Count; i++) {
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
  s_refreshAirlocks,
  s_checkAirlocks,
  s_processAirlocks,
 };
 current_state = 1; // skip initial refresh
 Me.SetCustomName("JAMS CPU");
 hideFromHud(Me);
 s_refreshAirlocks();
 updateAirlocks();
 state_cycle_counts = new int[states.Length];
}

void Main() {
 int num_states = 0;
 cycle_count = 0;
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
