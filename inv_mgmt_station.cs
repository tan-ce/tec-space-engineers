public class TecRoot {
    public const String term_title = "Station Automation";
    public const String ore_cargo_name  = "Ore & Ingot Cargo (x)";
    public const String main_cargo_name = "General Cargo (z)";
    
    protected static bool init_done = false;
    protected static IMyGridTerminalSystem GridTerminalSystem;
    protected static String _stdout;
    protected static IMyTextPanel _term;
    protected static bool heartbeat = true;
    
    public static void pr(String msg) {
        _stdout += msg + "\n";
    }
    
    public static void Init(IMyGridTerminalSystem gts) {
        GridTerminalSystem = gts;
        
        if (!init_done) {
            _term = (IMyTextPanel) GridTerminalSystem.GetBlockWithName("Terminal Output");
            init_done = true;
        }
        _stdout = term_title;
        
        if (heartbeat) {
            heartbeat = false;
            pr(" \\\n");
        } else { 
            heartbeat = true;
            pr(" /\n");
        }
    }
    
    public static void Terminate() {
        _term.WritePublicText(_stdout);
        _stdout = null;
        
        // Docking changes this object, so unset it to help garbage collection
        GridTerminalSystem = null;
    }
}

public class SolarController : TecRoot {
    public const float target = 0.9f;
    public const float maxOut = 0.12f;

    public SolarController() {
        // Get all solar panels
        List<IMyTerminalBlock> panels = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(panels);
        
        // Get current and total power output
        int panelCnt = 0;
        float curUse = 0f;  // Amount of power currently being used
        float genOut = 0f;  // Power output potential
        float lowestOut = 0f;
        
        for (int i = 0; i < panels.Count; i++) {
            IMySolarPanel panel = panels[i] as IMySolarPanel;
            if (panel != null && panel.IsFunctional) {
                panelCnt++;
                float cur = panel.CurrentOutput;
                float max = panel.MaxOutput;
                
                curUse += cur;
                genOut += max;
                
                if (lowestOut == 0f || max < lowestOut) {
                    lowestOut = max;
                }
            }
        }
        
        // Display status
        pr(String.Format("Solar Panel Count: {0}", panelCnt));
        pr(String.Format("Solar Power Usage: {0:0.0}/{1:0.0} MW", curUse, genOut));

        // Nothing else to do if no panels
        if (panelCnt == 0) return;

        // Rotor control
        IMyMotorStator rotor = (IMyMotorStator) GridTerminalSystem.GetBlockWithName("Solar Panel Rotor");
        float thresh = target * maxOut;
        if (lowestOut < thresh) {
            float speed = ((thresh - lowestOut) / thresh) * 1.5f;
            if (speed < 0.05f) speed = 0.05f;
            rotor.SetValue("Velocity", speed);
            rotor.ApplyAction("OnOff_On");
            pr(String.Format("Rotor Status: On ({0:0.00} rpm)", speed));
        } else {
            rotor.ApplyAction("OnOff_Off");
            pr("Rotor Status: Off");
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
    TecRoot.Init(GridTerminalSystem);
    
    new SolarController();
    TecRoot.pr("---");
    new InvSorter();
    
    TecRoot.Terminate();
}