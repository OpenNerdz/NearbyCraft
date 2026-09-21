using NearbyCraft;

internal static class StorageOutputTests
{
    internal static void Run(Action<bool,string> check)
    {
        ItemStack Stack(int type,int count) => new ItemStack(new ItemValue{type=type},count);
        long Count(ItemStack[] slots,int type) => slots.Where(x=>x!=null && !x.IsEmpty() && x.itemValue.type==type).Sum(x=>(long)x.count);
        var source=new[]{Stack(1,20),Stack(2,50),Stack(3,30)};
        var allowed=new[]{2};
        var packed=new PackedBoolArray(3);
        check(StorageOutputPlanner.HasExportable(source,packed,allowed),"Output preflight finds allowed ore");
        packed[1]=true;
        check(!StorageOutputPlanner.HasExportable(source,packed,allowed),"Output preflight skips reserved ore");
        check(!StorageOutputPlanner.HasExportable(null,null,allowed)
            && !StorageOutputPlanner.HasExportable(source,null,null),"Output preflight tolerates missing arrays");
        check(!StorageOutputPlanner.HasExportable(new[]{Stack(1,500)},null,allowed),"Fuel-only hopper skips network scan");
        var denied=Stack(2,5);denied.itemValue.ItemClassOrMissing.CanStore=false;
        check(!StorageOutputPlanner.HasExportable(new[]{denied},null,allowed),"Unstorable output skips scan");
        for(int i=0;i<1000;i++) StorageOutputPlanner.HasExportable(source,packed,allowed);
        long startBytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) StorageOutputPlanner.HasExportable(source,packed,allowed);
        long guardBytes=GC.GetAllocatedBytesForCurrentThread()-startBytes;
        check(guardBytes==0,"10,000 reserved-output preflights allocate zero managed bytes");
        Console.WriteLine($"Output preflight: {guardBytes} allocated bytes / 10,000 checks (pure-rule test).");
        var dest=new[]{Stack(2,95)};
        var after=ItemStack.Clone(source);
        var plan=new StorageTransferPlan();plan.Add(dest,new bool[1]);
        int moved=StorageOutputPlanner.Collect(plan,after,new bool[3],new[]{2});
        check(moved==5 && after[1].count==45,"Output export retains full-box remainder");
        check(source[1].count==50 && dest[0].count==95,"Output planning does not alter live inventories");
        check(plan.TryCommit(_=>true) && dest[0].count==100,"Output destination commits exact fitting quantity");
        check(after[0].count==20 && after[2].count==30,"Fuel and unrelated items are never exported");
        after=ItemStack.Clone(source);plan=new StorageTransferPlan();plan.Add(new[]{ItemStack.Empty},new bool[1]);
        check(StorageOutputPlanner.Collect(plan,after,new[]{false,true,false},new[]{2})==0,"Reserved source slot is never exported");
        after=ItemStack.Clone(source);plan=new StorageTransferPlan();plan.Add(new[]{ItemStack.Empty},new[]{true});
        check(StorageOutputPlanner.Collect(plan,after,new bool[3],new[]{2})==0,"Reserved destination slot is never filled");

        var random=new Random(7110);
        for(int n=0;n<10000;n++)
        {
            source=Enumerable.Range(0,random.Next(1,31)).Select(_=>Stack(random.Next(1,7),random.Next(1,101))).ToArray();
            dest=Enumerable.Range(0,random.Next(1,31)).Select(_=>random.Next(3)==0?ItemStack.Empty:Stack(random.Next(1,7),random.Next(1,101))).ToArray();
            var sourceLocks=source.Select(_=>random.Next(5)==0).ToArray();
            packed=new PackedBoolArray(source.Length);
            for(int i=0;i<source.Length;i++) packed[i]=sourceLocks[i];
            bool expected=source.Where((s,i)=>!sourceLocks[i]).Any(s=>s.itemValue.type>=2 && s.itemValue.type<=5);
            check(StorageOutputPlanner.HasExportable(source,packed,new[]{2,3,4,5})==expected,"Randomized output preflight agrees with unlocked ore");
            var destLocks=dest.Select(_=>random.Next(5)==0).ToArray();
            var sourceBefore=ItemStack.Clone(source);var destBefore=ItemStack.Clone(dest);after=ItemStack.Clone(source);
            plan=new StorageTransferPlan();plan.Add(dest,destLocks);
            moved=StorageOutputPlanner.Collect(plan,after,sourceLocks,new[]{2,3,4,5});
            for(int i=0;i<source.Length;i++) check(StorageTransferPlan.ExactEquals(source[i],sourceBefore[i]),"Export plan leaves source untouched");
            bool stale=n%7==0;
            bool committed=plan.TryCommit(_=>!stale);
            check(committed!=stale,"Stale destination rejected before commit");
            if(!committed)
            {
                for(int i=0;i<dest.Length;i++) check(StorageTransferPlan.ExactEquals(dest[i],destBefore[i]),"Stale export changed no destination slot");
                continue;
            }
            for(int type=1;type<=6;type++)
                check(Count(sourceBefore,type)+Count(destBefore,type)==Count(after,type)+Count(dest,type),"Export conservation for every item type");
            check(Count(sourceBefore,1)==Count(after,1) && Count(sourceBefore,6)==Count(after,6),"Only requested ore types exported");
            check(sourceBefore.Sum(x=>x.count)-after.Sum(x=>x.count)==moved,"Moved count matches source debit");
            for(int i=0;i<source.Length;i++) if(sourceLocks[i]) check(StorageTransferPlan.ExactEquals(sourceBefore[i],after[i]),"Source locks conserved");
            for(int i=0;i<dest.Length;i++)
            {
                check(dest[i].count>=0 && dest[i].count<=100,"Export stack limit");
                if(destLocks[i]) check(StorageTransferPlan.ExactEquals(destBefore[i],dest[i]),"Destination locks conserved");
            }
        }
    }
}
