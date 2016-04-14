/*
 * JAMS v0.5alpha
 *
 * (JAMS Airlock Management System)
 *
 * Published under "do whatever you want with it" license (aka public domain).
 *
 */

const int STEP_INIT = 0;
const int STEP_DOOR_IN = 1;
const int STEP_DOOR_OUT = 2;
const int STEP_DOOR_CLOSE = 3;

public struct Airlock_State {
	public TimeSpan timestamp;
	public TimeSpan op_start;
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
	public int last_pressure;
	public int outer_sensor_idx; // -1 means we have no idea
};

public List<Airlock_State> airlock_states = null;

public List<Airlock_Group> airlock_groups = null;

// state machine
Func < bool > [] states = null;

bool init = false;
int current_state;
TimeSpan runtime;

string getBlockID(string val) {
	var regex = new System.Text.RegularExpressions.Regex("\\{([\\dA-F]+)\\}");
	var match = regex.Match(val);
	if (!match.Success || !match.Groups[1].Success) {
		throw new Exception("Unknown block id format");
	}
	return match.Groups[1].Value;
}

string groupToString(Airlock_Group ag) {
	StringBuilder sb = new StringBuilder();
	sb.Append(getBlockID(ag.vent.ToString()));
	sb.Append(":");
	sb.Append(getBlockID(ag.doors[0].ToString()));
	sb.Append(":");
	sb.Append(getBlockID(ag.doors[1].ToString()));
	sb.Append(":");
	sb.Append(getBlockID(ag.sensors[0].ToString()));
	sb.Append(":");
	sb.Append(getBlockID(ag.sensors[1].ToString()));
	sb.Append(":");
	sb.Append(ag.outer_sensor_idx);
	return sb.ToString();
}

void saveGroups() {
	StringBuilder sb = new StringBuilder();
	for (int i = 0; i < airlock_groups.Count; i++) {
		sb.Append(groupToString(airlock_groups[i]));
		if (i != airlock_groups.Count - 1)
			sb.Append(";");
	}
	Storage = sb.ToString();
}

void updateGroups() {
	if (Storage == "") {
		return;
	}
	string[] group_strs = Storage.Split(';');
	var skipList = new List<int>();

	for (int g = airlock_groups.Count - 1; g >= 0; g--) {
		var ag = airlock_groups[g];
		for (int i = group_strs.Length - 1; i >= 0; i--) {
			if (skipList.Contains(i)) {
				continue;
			}
			string group_str = group_strs[i];
			string[] strs = group_str.Split(':');
			string vent_id = strs[0];
			string door1_id = strs[1];
			string door2_id = strs[2];
			string sensor1_id = strs[3];
			string sensor2_id = strs[4];
			int outer_idx = Convert.ToInt32(strs[5]);

			if (getBlockID(ag.vent.ToString()) != vent_id) {
				continue;
			}
			if (getBlockID(ag.doors[0].ToString()) != door1_id) {
				continue;
			}
			if (getBlockID(ag.doors[1].ToString()) != door2_id) {
				continue;
			}
			if (getBlockID(ag.sensors[0].ToString()) != sensor1_id) {
				continue;
			}
			if (getBlockID(ag.sensors[1].ToString()) != sensor2_id) {
				continue;
			}
			if (outer_idx != -1)
				ag.outer_sensor_idx = outer_idx;

			skipList.Add(i);

			airlock_groups[g] = ag;
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

void tryLock(IMyDoor door) {
	if (door.OpenRatio == 0) {
		door.ApplyAction("OnOff_Off");
	}
}

int getPressure(IMyAirVent av) {
	var p_regex = new System.Text.RegularExpressions.Regex("Room pressure: ([\\d\\.]+)\\%");
	var p_match = p_regex.Match(av.DetailedInfo);
	var np_regex = new System.Text.RegularExpressions.Regex("Room pressure: (\\w+)");
	var np_match = np_regex.Match(av.DetailedInfo);

	if (!p_match.Success && !np_match.Success) {
		throw new Exception("Fail");
	}
	if (np_match.Groups[1].Value == "Not") {
		return -1;
	}

	Decimal p = new Decimal();
	bool result = Decimal.TryParse(p_match.Groups[1].Value, out p);
	if (!result) {
		throw new Exception("Invalid detailed info format!");
	}
	return Convert.ToInt32(p);
}

Nullable<Airlock_Group> parseGroup(IMyBlockGroup group) {
	var blocks = group.Blocks;
	Airlock_Group ag = new Airlock_Group();
	ag.outer_sensor_idx = -1;
	ag.last_pressure = -1;
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
		else if ((block as IMyAirVent) != null) {
			if (ag.vent != null) {
				Echo("Need to have one airvent");
				return null;
			}
			ag.vent = (block as IMyAirVent);
		}
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

	// figure out which sensor likely belongs to which door
	int min_idx = -1;
	double min_dist = 0;
	var door = ag.doors[0];
	for (int i = 0; i < 2; i++) {
		var d = Vector3D.Distance(door.GetPosition(), ag.sensors[i].GetPosition());
		if (d < min_dist || min_idx == -1) {
			min_dist = d;
			min_idx = i;
		}
	}
	ag.sensor_to_door_idx[min_idx] = 0;
	ag.sensor_to_door_idx[(min_idx + 1) % 2] = 1;

	// close and disable all doors
	close(ag.doors[0]);
	close(ag.doors[1]);
	tryLock(ag.doors[0]);
	tryLock(ag.doors[1]);
	depressurize(ag.vent);

	return ag;
}


bool s_checkSensors() {
	bool result = true;
	for (int i = 0; i < airlock_groups.Count; i++) {
		var ag = airlock_groups[i];

		// skip active airlocks
		bool active = false;
		for (int j = 0; j < airlock_states.Count; j++) {
			if (airlock_states[j].group_idx == i) {
				active = true;
				break;
			}
		}
		if (active) {
			result = false;
			continue;
		}

		for (int s_idx = 0; s_idx < ag.sensors.Count; s_idx++) {
			var sensor = ag.sensors[s_idx];
			if (sensor.IsActive) {
				// activate the airlock
				var state = new Airlock_State();
				state.group_idx = i;
				state.timestamp = runtime;
				state.op_start = runtime;
				state.step_id = 0;
				state.sensor_idx = s_idx;

				airlock_states.Add(state);
				break;
			}
		}
	}
	return result;
}

bool s_refreshState() {
	// don't refresh anything until we're idle, otherwise things get nasty
	if (airlock_states.Count != 0) {
		return true;
	}
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
		Airlock_Group ? ag = parseGroup(group);
		if (ag.HasValue) {
			airlock_groups.Add(ag.Value);
			new_groups.Add(ag.Value.name);
		}
		else
			Echo("Error parsing group " + group.Name);
	}
	updateGroups();
	// now clear the list of groups not present in new_groups
	for (int i = airlock_groups.Count - 1; i >= 0; i--) {
		var name = airlock_groups[i].name;
		if (!new_groups.Contains(name))
			airlock_groups.RemoveAt(i);
	}
	saveGroups();
	return false;
}

bool s_engageAirlock() {
	var death_row = new List<int>();
	// process all airlocks that are currently active
	for (int i = 0; i < airlock_states.Count; i++) {
		var state = airlock_states[i];
		var ag = airlock_groups[state.group_idx];

		// timeout
		if ((runtime - state.timestamp).Seconds > 10 || (runtime - state.op_start).Seconds > 10) {
			death_row.Add(i);
			continue;
		}

		// decide what to do
		if (state.step_id == STEP_INIT) {
			bool ready = false;

			bool pressureSet = false;

			if (state.sensor_idx == ag.outer_sensor_idx || ag.outer_sensor_idx == -1) {
				depressurize(ag.vent);

				if (getPressure(ag.vent) == 0) {
					pressureSet = true;
				}
			} else {
				pressurize(ag.vent);

				if (getPressure(ag.vent) == 100) {
					pressureSet = true;
				}
			}

			// if the vent is already (de)pressurizing, wait until it's fully
			// (de)pressurized, or just go to next stage if it's stuck
			if (pressureSet ||
					((runtime - state.op_start).Seconds > 5 && getPressure(ag.vent) == ag.last_pressure)) {
				ready = true;
			}
			ag.last_pressure = getPressure(ag.vent);

			// if we're ready, open the door and proceed to next state
			if (ready) {
				var door_idx = ag.sensor_to_door_idx[state.sensor_idx];
				var door = ag.doors[door_idx];
				// make sure we do open the door
				open(door);
				state.timestamp = runtime;
				state.op_start = runtime;
				ag.last_pressure = -1;
				state.step_id = STEP_DOOR_IN;
			}
		}

		if (state.step_id == STEP_DOOR_IN) {
			// wait 1 second after sensor stops being active, then close the
			// door and start pressurizing/depressurizing

			var sensor = ag.sensors[state.sensor_idx];
			var door_idx = ag.sensor_to_door_idx[state.sensor_idx];
			var door = ag.doors[door_idx];

			// if sensor is still active, update the timestamp
			if (door.OpenRatio != 0 && sensor.IsActive) {
				state.timestamp = runtime;
			}

			var diff = runtime - state.timestamp;
			if (diff.Seconds > 1) {

				// if vent is depressurized, it's outer door
				if (ag.outer_sensor_idx == -1) {
					if (!ag.vent.IsPressurized()) {
						ag.outer_sensor_idx = state.sensor_idx;
					} else {
						ag.outer_sensor_idx = (state.sensor_idx + 1) % 2;
					}
				}

				// close the door
				if (door.OpenRatio != 0 && (runtime - state.op_start).Seconds < 6) {
					close(door);
				} else {
					close(door);
					tryLock(door);

					// if it was an outer door, pressurize
					if (state.sensor_idx == ag.outer_sensor_idx) {
						pressurize(ag.vent);
					} else {
						depressurize(ag.vent);
					}
					// update sensor id
					ag.last_pressure = -1;
					state.timestamp = runtime;
					state.op_start = runtime;
					state.sensor_idx = (state.sensor_idx + 1) % 2;
					state.step_id = STEP_DOOR_OUT;
				}
			}
		}
		if (state.step_id == STEP_DOOR_OUT) {
			// wait until the room is fully pressurized/depressurized
			bool pressureSet = false;
			if (state.sensor_idx == ag.outer_sensor_idx && getPressure(ag.vent) == 0) {
				pressureSet = true;
			} else if (state.sensor_idx != ag.outer_sensor_idx && getPressure(ag.vent) == 100) {
				pressureSet = true;
			}
			if (pressureSet ||
					((runtime - state.op_start).Seconds > 5 &&
					getPressure(ag.vent) == ag.last_pressure)) {
				var door_idx = ag.sensor_to_door_idx[state.sensor_idx];
				var door = ag.doors[door_idx];
				// open the door
				open(door);
				state.timestamp = runtime;
				state.op_start = runtime;
				state.step_id = STEP_DOOR_CLOSE;
			}
			ag.last_pressure = getPressure(ag.vent);
		}
		if (state.step_id == STEP_DOOR_CLOSE) {
			var door_idx = ag.sensor_to_door_idx[state.sensor_idx];
			var door = ag.doors[door_idx];

			// wait until sensor is activated, or wait for 2 seconds
			var sensor = ag.sensors[state.sensor_idx];
			if (sensor.IsActive) {
				state.timestamp = runtime;
			}
			var diff = runtime - state.timestamp;
			if (diff.Seconds > 2) {
				if (door.OpenRatio != 0) {
					close(door);
				} else {
					tryLock(door);
					death_row.Add(i);
				}
			}
		}
		airlock_states[i] = state;
		airlock_groups[state.group_idx] = ag;
	}
	for (int i = death_row.Count - 1; i >= 0; i--) {
		var state = airlock_states[i];
		var ag = airlock_groups[state.group_idx];
		close(ag.doors[0]);
		tryLock(ag.doors[0]);
		close(ag.doors[1]);
		tryLock(ag.doors[1]);
		depressurize(ag.vent);
		airlock_states.RemoveAt(i);
	}
	return false;
}

void Main() {
	if (!init) {
		states = new Func < bool > [] {
			s_refreshState,
			s_checkSensors,
			s_engageAirlock,
		};
		airlock_states = new List<Airlock_State>();
		airlock_groups = new List<Airlock_Group>();
		current_state = 0;
		init = true;
		runtime = TimeSinceLastRun  - TimeSinceLastRun ; // 0
	} else {
		runtime += TimeSinceLastRun;
	}
	bool result;
	do {
		result = states[current_state]();
		current_state = (current_state + 1) % states.Length;
	} while (result);
}
