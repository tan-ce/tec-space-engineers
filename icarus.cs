public class TecRoot {
    public const String term_title = "Ship Automation";
    public const String ore_cargo_name  = "Cargo Container 3 (raw)";
    public const String main_cargo_name = "Cargo Container 1 (z)";
    
    protected const bool debug_mode = false;
    
    protected const int ctr_thresh = 10;
    protected const int sec_thresh = 6;
    
    protected static bool init_done = false;
    protected static IMyGridTerminalSystem GridTerminalSystem;
    protected static String _stdout;
    protected static String _dbgout = "";
    protected static IMyTextPanel _term;
    protected static IMyTextPanel _dbg_term;
    protected static bool heartbeat = true;
    
    protected static int ctr = 0;
    protected static int sec = 0;
    
    protected static bool is_sec_run;
    
    public static void dpr(String msg, bool nonl = false) {
        if (!debug_mode) return;
        if (nonl) _dbgout += msg;
        else _dbgout += msg + "\n";        
        
        _dbg_term.WritePublicText(_dbgout);
    }
    
    public static void pr(String msg, bool nonl = false) {
        if (nonl) _stdout += msg;
        else _stdout += msg + "\n";
    }
    
    protected static void TermInit() {
        if (!init_done) {
            _term = (IMyTextPanel) GridTerminalSystem.GetBlockWithName("Terminal Output");
            if (debug_mode) {
                _dbg_term = (IMyTextPanel) GridTerminalSystem.GetBlockWithName("Debug Terminal");
            }

            init_done = true;
        }

        if (is_sec_run) {
            _stdout = "";
            if (debug_mode) _dbg_term.WritePublicText(_dbgout);
        }
    }
    
    // If false is returned, main() should immediately return
    public static bool Init(IMyGridTerminalSystem gts) {
        ctr++;
        if (ctr <= ctr_thresh) return false;
        ctr = 0;
        
        sec++;
        if (sec > sec_thresh) {
            sec = 0;
            is_sec_run = true;
        } else {
            is_sec_run = false;
        }
        
        GridTerminalSystem = gts;
        TermInit();
        
        return true;
    }
    
    public static void InitHeadless(IMyGridTerminalSystem gts) {
        GridTerminalSystem = gts;
        TermInit();
    }

    public static void TerminateHeadless() {
        // Docking changes this object, so unset it to help garbage collection
        GridTerminalSystem = null;
    }
    
    public static void Terminate() {
        // Generate title line
        String title_line, hb;
        if (heartbeat) {
            heartbeat = false;
            hb = "\\";
        } else { 
            heartbeat = true;
            hb = "/";
        }
        if (is_sec_run) {
            title_line = String.Format("{0} #\n\n", term_title);
        } else {
            title_line = String.Format("{0} {1}\n\n", term_title, hb);
        }
        
        _term.WritePublicText(title_line + _stdout);
        _stdout = null;
        
        // Docking changes this object, so unset it to help garbage collection
        GridTerminalSystem = null;
    }
}

public class LightSetter : TecRoot {
    public LightSetter() {
        List<IMyTerminalBlock> lights = new List<IMyTerminalBlock>();
        GridTerminalSystem.SearchBlocksOfName("Interior Light", lights);
        Color c = new Color(244, 255, 250, 255);
        for (int i = 0; i < lights.Count; i++) {
            lights[i].SetValueFloat("Intensity", 0.5f);
            lights[i].SetValueFloat("Radius", 9.0f);
            lights[i].SetValueFloat("Falloff", 2.0f);
            lights[i].SetValue("Color", c);
        }
    }
}

public class AirlockControl : TecRoot {
    
    protected IMyDoor intDoor;
    protected IMyDoor extDoor;
    protected IMyAirVent vent;
    
    protected bool intClosed;
    protected bool intOpened;
    protected bool extClosed;
    protected bool extOpened;
    protected bool evacDone;
    protected bool presDone;
    
    protected float ventLevel;
    
    protected string baseName;
    
    protected const int vsUnknown = 0;
    protected const int vsOn = 1;
    protected const int vsOff = -1;

    protected void LoadAirlock(string baseName) {
        string ventName = String.Format("Air Vent ({0} Airlock)", baseName);
        string intDoorName = String.Format("Airlock Door: Int. ({0})", baseName);
        string extDoorName = String.Format("Airlock Door: Ext. ({0})", baseName);
        
        vent = (IMyAirVent) GridTerminalSystem.GetBlockWithName(ventName);
        intDoor = (IMyDoor) GridTerminalSystem.GetBlockWithName(intDoorName);
        extDoor = (IMyDoor) GridTerminalSystem.GetBlockWithName(extDoorName);

        if (vent == null) {
            throw new Exception("Missing airvent: " + ventName);
        }
        if (intDoor == null) {
            throw new Exception("Missing int. door: " + intDoorName);
        }
        if (extDoor == null) {
            throw new Exception("Missing ext. door: " + extDoorName);
        }
        
        intClosed = intDoor.OpenRatio == 0f;
        intOpened = intDoor.OpenRatio == 1f;
        extClosed = extDoor.OpenRatio == 0f;
        extOpened = extDoor.OpenRatio == 1f;
        
        ventLevel = vent.GetOxygenLevel();
        evacDone = ventLevel < 0.00001;
        presDone = ventLevel > 0.99;
    }

    private void CloseDoor(IMyDoor door) {
        door.ApplyAction("Open_Off");
    }
    
    protected void CloseAllDoors() {
        CloseDoor(intDoor);
        CloseDoor(extDoor);
    }

    protected void OpenDoor(IMyDoor door) {
        door.ApplyAction("Open_On");
    }
    
    protected void DisableVent() {
        vent.ApplyAction("OnOff_Off");
        vent.ApplyAction("Depressurize_Off");
    }
    
    protected void ToExterior() {
        vent.ApplyAction("Depressurize_On");
        vent.ApplyAction("OnOff_On");
    }
    
    protected void ToInterior() {
        vent.ApplyAction("Depressurize_Off");
        vent.ApplyAction("OnOff_On");
    }
    
    protected void StateChange() {
        if (vent.Enabled) {
            if (vent.IsDepressurizing) {
                // Intend to open to exterior
                if (extOpened && intClosed) {
                    DisableVent();
                    dpr("To Exterior Complete");
                } else if (evacDone) {
                    OpenDoor(extDoor);
                } else {
                    CloseAllDoors();
                }
            } else {
                // Intend to open to interior
                if (intOpened && extClosed) {
                    DisableVent();
                    dpr("To Interior Complete");
                } else if (presDone) {
                    OpenDoor(intDoor);
                } else {
                    CloseAllDoors();
                }
            }
        } else {
            // If both doors are open, close them immediately
            if (!intClosed && !extClosed) {
                CloseAllDoors();
            }
        }
    }
    
    protected void PrintStatus() {
        pr(baseName + " Airlock: ", /* nonl */ true);
        pr(String.Format("Pressure at {0:0.0}%", ventLevel * 100f));
    }
    
    /*
     * argv[0] - "airlock"
     * argv[1] - event name
     *      "to_exterior"
     *      "to_interior"
     *      "state_change"  - State change, door and vent events should trigger this
     * argv[2] - always ignored
     * argv[3...] - airlock name
     * The components of the airlock must be named:
     *      "Air Vent (<name> airlock)"
     *      "Airlock Door: Int. (<name>)"
     *      "Airlock Door: Ext. (<name>)"
     */
    public AirlockControl(string[] argv) {
        baseName = argv[3];
        for (int i = 4; i < argv.Length; i++) baseName += " " + argv[i];
        LoadAirlock(baseName);

        if (argv[1].Equals("to_exterior")) ToExterior();
        else if (argv[1].Equals("to_interior")) ToInterior();
        else if (argv[1].Equals("state_change")) StateChange();
    }
    
    public static void AirlockTimerTrigger() {
        string[] airlocks = new string[] {
            "Mess"
        };

        for (int i = 0; i < airlocks.Length; i++) {
            string[] argv = new string[] {"airlock", "state_change", "ign", airlocks[i]};
            AirlockControl ctrl = new AirlockControl(argv);
            ctrl.PrintStatus();
        }
    }
}

public class GasStatus : TecRoot {
    public GasStatus() {
        // H2
        List<IMyTerminalBlock> H2Tanks = new List<IMyTerminalBlock>();
        GridTerminalSystem.SearchBlocksOfName("Hydrogen Tank", H2Tanks);
        float h2level = 0f;
        if (H2Tanks.Count == 0) {
            pr("H2 Fuel: None");
        } else {
            for (int i = 0; i < H2Tanks.Count; i++) {
                h2level += ((IMyOxygenTank) H2Tanks[i]).GetOxygenLevel();
            }
            h2level = h2level / H2Tanks.Count;
            pr(String.Format("H2 Fuel: {0:00.00}% ({1} tanks)",
                h2level * 100f, H2Tanks.Count));
        }
        
        // O2
        List<IMyTerminalBlock> O2Tanks = new List<IMyTerminalBlock>();
        GridTerminalSystem.SearchBlocksOfName("Oxygen Tank", O2Tanks);
        float o2level = 0f;
        if (O2Tanks.Count == 0) {
            pr("O2 Reserves: None");
        } else {
            for (int i = 0; i < O2Tanks.Count; i++) {
                o2level += ((IMyOxygenTank) O2Tanks[i]).GetOxygenLevel();
            }
            o2level = o2level / O2Tanks.Count;
            pr(String.Format("O2 Reserves: {0:00.00}% ({1} tanks)",
                o2level * 100f, O2Tanks.Count));
        }
    }
}

public class InvSorter : TecRoot {
    IMyInventory OreInv = null;
    IMyInventory MainInv = null;
    List<IMyInventory> OtherInv = new List<IMyInventory>();
    
    void SortPeriphInv(IMyInventory inv) {
        List<IMyInventoryItem> items = inv.GetItems();
        bool main_full = false;
        bool ore_full = false;
        
        for (int i = items.Count - 1; i >= 0; i--) {
            IMyInventoryItem item = items[i];
            
            switch (items[i].Content.TypeId.ToString()) {
            case "MyObjectBuilder_Ore":
            case "MyObjectBuilder_Ingot":
                if (inv == OreInv) {
                    continue;
                }
                if (OreInv.IsFull) {
                    ore_full = true;
                    continue;
                }
                inv.TransferItemTo(OreInv, i, stackIfPossible: true);
                break;
                
            default:
                if (inv != MainInv) {
                    if (MainInv.IsFull) {
                        main_full = true;
                    } else {
                        inv.TransferItemTo(MainInv, i, stackIfPossible: true);
                    }
                }
                break;
            }
        }
        
        if (main_full) pr("Warning: Main storage full");
        if (ore_full) pr("Warning: Ore storage full");
    }
    
    void AddInvOfType<T>(int idx) {
        List<IMyTerminalBlock> TermBlks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocksOfType<T>(TermBlks);
        for (int i = 0; i < TermBlks.Count; i++) {
            OtherInv.Add(TermBlks[i].GetInventory(idx));
        }
    }
    
    protected static IMyInventory GetInvWithName(string name, int idx) {
        IMyTerminalBlock blk = GridTerminalSystem.GetBlockWithName(name);
        if (blk == null) return null;
        return blk.GetInventory(idx);
    }
    
    public InvSorter() {
        List<IMyTerminalBlock> TermBlks;

        // Add Main and Ore Cargo Container
        OreInv = GetInvWithName(ore_cargo_name, 0);
        MainInv = GetInvWithName(main_cargo_name, 0);

        // Check that we have indeed found the Ore and Main Cargo holds
        if (OreInv == null || MainInv == null) {
            pr("Error: Could not find either Ore or Main cargo holds");
            pr("Inventory Management: Error");
            return;
        }

        // Add Main and Ore Cargo Containers
        OtherInv.Add(OreInv);
        OtherInv.Add(MainInv);

        // Get all access ports
        TermBlks = new List<IMyTerminalBlock>();
        GridTerminalSystem.SearchBlocksOfName("Cargo Access", TermBlks);
        for (int i = 0; i < TermBlks.Count; i++) {
            OtherInv.Add(TermBlks[i].GetInventory(0));
        }
        
        // Get connected miner cargo
        TermBlks = new List<IMyTerminalBlock>();
        GridTerminalSystem.SearchBlocksOfName("Miner Cargo", TermBlks);
        for (int i = 0; i < TermBlks.Count; i++) {
            OtherInv.Add(TermBlks[i].GetInventory(0));
        }

        // Add various other inventories to be sorted
        AddInvOfType<IMyRefinery>(1);
        AddInvOfType<IMyAssembler>(1);
        AddInvOfType<IMyShipConnector>(0);
        
        // Send the stuff to the right places
        for (int i = 0; i < OtherInv.Count; i++) {
            SortPeriphInv(OtherInv[i]);
        }
        
        pr("Inventory Management: Online");
    }
}

void Main(String arg) {
    if (arg.Length > 0) {
        TecRoot.InitHeadless(GridTerminalSystem);
        
        string[] argv = arg.Split(' ');
        
        if (argv[0].Equals("airlock")) {
            new AirlockControl(argv);
        }
        
        TecRoot.TerminateHeadless();
    } else {
        if (!TecRoot.Init(GridTerminalSystem)) return;
        
        AirlockControl.AirlockTimerTrigger();
        new GasStatus();
        new InvSorter();
        
        TecRoot.Terminate();
    }
}