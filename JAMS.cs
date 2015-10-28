/*
 * JAMS v0.1alpha
 *
 * (JAMS Airlock Management System)
 *
 * Published under "do whatever you want with it" license (aka public domain).
 *
 */

const int STEP_OUTER_AIRVENT = 0;
const int STEP_OUTER_AIRVENT_WAIT = 1;
const int STEP_OUTER_DOOR = 2;
const int STEP_INNER_AIRVENT = 3;
const int STEP_INNER_DOOR = 4;

public struct Airlock_State {
	public TimeSpan timestamp;
	public int step_id;
	public int group_idx;
	public int sensor_idx;
}

public struct Airlock_Group {
	public string name;
	public List<IMyDoor> doors;
	public List<IMySensorBlock> sensors;
	public Dictionary<int,int> sensor_to_door_idx; // maps sensor idx to door idx
	public List<IMyLightingBlock> lights;
	public IMyAirVent vent;
	public int outer_sensor_idx; // -1 means we have no idea
};

Airlock_State ? cur_airlock_state = null;

List<Airlock_Group> airlock_groups = null;
List<int> waiting_queue = null;
bool mutex = false;

// state machine
Func < bool > [] states = null;

bool init = false;
int current_state;
TimeSpan runtime;

double distance(Vector3I v1, Vector3I v2) {
	return Math.Sqrt(Math.Pow(v1.X - v2.X, 2) + Math.Pow(v1.Y - v2.Y, 2) + Math.Pow(v1.Z - v2.Z, 2));
}

Nullable<Airlock_Group> parseGroup(IMyBlockGroup group) {
	var blocks = group.Blocks;
	Airlock_Group ag = new Airlock_Group();
	ag.outer_sensor_idx = -1;
	ag.doors = new List<IMyDoor>();
	ag.sensors = new List<IMySensorBlock>();
	ag.sensor_to_door_idx = new Dictionary<int,int>();
	ag.lights = new List<IMyLightingBlock>();
	ag.vent = null;
	ag.name = group.Name;
	
	// we need find two doors and two sensors
	for (int i = 0; i < blocks.Count; i++) {
		var block = blocks[i];
		if ((block as IMyDoor) != null)
			ag.doors.Add(block as IMyDoor);
		else if ((block as IMySensorBlock) != null)
			ag.sensors.Add(block as IMySensorBlock);
		else if ((block as IMyAirVent) != null)
			ag.vent = (block as IMyAirVent);
		else if ((block as IMyLightingBlock) != null)
			ag.lights.Add(block as IMyLightingBlock);
		else {
			Echo("Unexpected block: " + block.CustomName);
			return null; // unexpected block
		}
	}
	if (ag.doors.Count != 2) {
		Echo("Need to have two doors");
		return null;
	}
	if (ag.sensors.Count != 2) {
		Echo("Need to have two sensors");
		return null;
	}
	if (ag.vent == null) {
		Echo("No air vent found");
		return null;
	}
	
	// figure out which sensor likely belongs to which block
	int min_idx = -1;
	double min_dist = 0;
	var door = ag.doors[0];
	for (int i = 0; i < 2; i++) {
		var d = distance(door.Position, ag.sensors[i].Position);
		if (d < min_dist || min_idx == -1) {
			min_dist = d;
			min_idx = i;
		}
	}
	ag.sensor_to_door_idx[min_idx] = 0;
	ag.sensor_to_door_idx[(min_idx + 1) % 2] = 1;
	
	return ag;
}

bool s_checkSensors() {
	if (!mutex && waiting_queue.Count > 0) {
		int new_idx = waiting_queue[0];
		waiting_queue.Remove(0);
		mutex = true; // take the lock
		var state = new Airlock_State();
		state.group_idx = new_idx;
		state.timestamp = runtime;
		state.step_id = 0;
		cur_airlock_state = state;
	}
	for (int i = 0; i < airlock_groups.Count; i++) {
		var ag = airlock_groups[i];
		// skip current group
		if (cur_airlock_state.HasValue && cur_airlock_state.Value.group_idx == i)
			continue;
		for (int s_idx = 0; s_idx < ag.sensors.Count; s_idx++) {
			var sensor = ag.sensors[s_idx];
			if (sensor.IsActive) {
				if (!mutex && waiting_queue.Count == 0) {
					mutex = true; // take the lock
					var state = new Airlock_State();
					state.group_idx = i;
					state.timestamp = runtime;
					state.step_id = 0;
					state.sensor_idx = s_idx;
					
					// do we know if we are an inner or an outer door?
					if (s_idx == ag.outer_sensor_idx) {
						// if we're an outer door, depressurize the vent
						StringBuilder builder = new StringBuilder();
						ag.vent.GetActionWithName("Depressurize").WriteValue(ag.vent, builder);
						if (builder.ToString() == "Off") {
							ag.vent.ApplyAction("Depressurize");
							throw new Exception("Test");
						}
					} else {
						// otherwise, pressurize it
						StringBuilder builder = new StringBuilder();
						ag.vent.GetActionWithName("Depressurize").WriteValue(ag.vent, builder);
						if (builder.ToString() == "On") {
							ag.vent.ApplyAction("Depressurize");
						}
					}
					cur_airlock_state = state;
				} else {
					if (!waiting_queue.Contains(i))
						waiting_queue.Add(i);
				}
			}
		}
	}
	return false;
}

bool s_refreshState() {
	if (mutex)
		return false;
	HashSet<string> prev_groups = new HashSet<string>();
	HashSet<string> new_groups = new HashSet<string>();
	for (int i = 0; i < airlock_groups.Count; i++) {
		prev_groups.Add(airlock_groups[i].name);
	}
	var groups = new List<IMyBlockGroup>();
	GridTerminalSystem.GetBlockGroups(groups);
	for (int i = 0; i < groups.Count; i++) {
		var group = groups[i];
		// skip groups we don't want
		if (!group.Name.StartsWith("JAMS")) {
			continue;
		}
		// if we already have this group, just add it to the list
		if (prev_groups.Contains(group.Name)) {
			new_groups.Add(group.Name);
			continue;
		}
		Airlock_Group ? ag = parseGroup(group);
		if (ag.HasValue) {
			airlock_groups.Add(ag.Value);
			new_groups.Add(ag.Value.name);
		}
		else
			Echo("Error parsing group " + group.Name);
	}
	// now clear the list of groups not present in new_groups
	for (int i = airlock_groups.Count - 1; i >= 0; i--) {
		var name = airlock_groups[i].name;
		if (!new_groups.Contains(name))
			airlock_groups.RemoveAt(i);
	}
	return false;
}

bool s_engageAirlock() {
	if (cur_airlock_state.HasValue) {
		if (cur_airlock_state.Value.step_id == STEP_OUTER_AIRVENT) {
			var diff = runtime - cur_airlock_state.Value.timestamp;
			if (diff.Seconds > 2) {
				// open the door and go to next state
				var group = airlock_groups[cur_airlock_state.Value.group_idx];
				var door_idx = group.sensor_to_door_idx[cur_airlock_state.Value.sensor_idx];
				var door = group.doors[door_idx];
				if (!door.Open)
					door.ApplyAction("Open");
				// update our state
				Airlock_State state = cur_airlock_state.Value;
				if (group.outer_sensor_idx == -1)
					state.step_id = STEP_OUTER_AIRVENT_WAIT;
				else
					state.step_id = STEP_OUTER_DOOR;
				state.timestamp = runtime;
				cur_airlock_state = state;
			}
		} else if (cur_airlock_state.Value.step_id == STEP_OUTER_AIRVENT_WAIT) {
			var group = airlock_groups[cur_airlock_state.Value.group_idx];
			var sensor = group.sensors[cur_airlock_state.Value.sensor_idx];
			var diff = runtime - cur_airlock_state.Value.timestamp;
			
			if (diff.Seconds > 1) {
				if (group.outer_sensor_idx == -1) {
					// if the vent is pressurized, it's an inner door
					if (!group.vent.IsPressurized()) {
						group.outer_sensor_idx = cur_airlock_state.Value.sensor_idx;
					} else {
						group.outer_sensor_idx = (cur_airlock_state.Value.sensor_idx + 1) % 2;
					}
					airlock_groups[cur_airlock_state.Value.group_idx] = group;
				}
				// update our state
				Airlock_State state = cur_airlock_state.Value;
				state.step_id = STEP_OUTER_DOOR;
				state.timestamp = runtime;
				cur_airlock_state = state;
			}
		} else if (cur_airlock_state.Value.step_id == STEP_OUTER_DOOR) {
			var group = airlock_groups[cur_airlock_state.Value.group_idx];
			var sensor = group.sensors[cur_airlock_state.Value.sensor_idx];
			var diff = runtime - cur_airlock_state.Value.timestamp;
			
			if (diff.Seconds > 9 || !sensor.IsActive) {
				// close the door, pressurize and go to next state
				var door_idx = group.sensor_to_door_idx[cur_airlock_state.Value.sensor_idx];
				var door = group.doors[door_idx];
				if (door.Open)
					door.ApplyAction("Open");
				
				if (cur_airlock_state.Value.sensor_idx == group.outer_sensor_idx) {
					StringBuilder builder = new StringBuilder();
					group.vent.GetActionWithName("Depressurize").WriteValue(group.vent, builder);
					if (builder.ToString() == "On") {
						group.vent.ApplyAction("Depressurize");
					}
				} else {
					StringBuilder builder = new StringBuilder();
					group.vent.GetActionWithName("Depressurize").WriteValue(group.vent, builder);
					if (builder.ToString() == "Off") {
						group.vent.ApplyAction("Depressurize");
					}
				}
				// update our state
				Airlock_State state = cur_airlock_state.Value;
				state.step_id = STEP_INNER_AIRVENT;
				state.timestamp = runtime;
				cur_airlock_state = state;
			}
		} else if (cur_airlock_state.Value.step_id == STEP_INNER_AIRVENT) {
			var group = airlock_groups[cur_airlock_state.Value.group_idx];
			var diff = runtime - cur_airlock_state.Value.timestamp;
			
			if (diff.Seconds > 2) {
				// open the door
				var door_idx = group.sensor_to_door_idx[(cur_airlock_state.Value.sensor_idx + 1) % 2];
				var door = group.doors[door_idx];
				if (!door.Open)
					door.ApplyAction("Open");
				
				// update our state
				Airlock_State state = cur_airlock_state.Value;
				state.step_id = STEP_INNER_DOOR;
				state.timestamp = runtime;
				cur_airlock_state = state;
			}
		} else if (cur_airlock_state.Value.step_id == STEP_INNER_DOOR) {
			var group = airlock_groups[cur_airlock_state.Value.group_idx];
			var sensor = group.sensors[(cur_airlock_state.Value.sensor_idx + 1) % 2];
			var diff = runtime - cur_airlock_state.Value.timestamp;
			
			if (diff.Seconds > 3) {
				// close the door
				var door_idx = group.sensor_to_door_idx[(cur_airlock_state.Value.sensor_idx + 1) % 2];
				var door = group.doors[door_idx];
				if (door.Open)
					door.ApplyAction("Open");
				
				// update our state
				cur_airlock_state = null;
				mutex = false;
			}
		}
	}
	return true;
}

void Main() {
	if (!init) {
		states = new Func < bool > [] {
			s_refreshState,
			s_checkSensors,
			s_engageAirlock,
		};
		airlock_groups = new List<Airlock_Group>();
		waiting_queue = new List<int>();
		current_state = 0;
		init = true;
		runtime = ElapsedTime - ElapsedTime; // 0
	} else {
		runtime += ElapsedTime;
	}
	bool result;
	do {
		result = states[current_state]();
		current_state = (current_state + 1) % states.Length;
	} while (!result);
}