using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Neo.SmartContract
{
    public class Option : Framework.SmartContract
    {
        //Option Settings
        public static string Name() => "staff option";
        public static string Symbol() => "SymbolOfTheOption";
        public static readonly byte[] Owner = "ATrzHaicmhRj15C3Vv6e6gLfLqhSD2PtTr".ToScriptHash();
        public static byte Decimals() => 8;
        private const ulong factor = 100000000; //decided by Decimals()
        private const ulong neo_decimals = 100000000;

        //Option Settings
        private static readonly byte[] neo_asset_id = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private const ulong total_amount = 100000000 * factor; // total option amount
        private const ulong pre_option_cap = 30000000 * factor; // pre ico option amount
        private const ulong basic_rate = 1000 * factor;
        private const int option_start_time = 1506787200;
        private const int option_end_time = 1538323200;


        public delegate void MyAction<T, T1>(T p0, T1 p1);
        public delegate void MyAction<T, T1, T2>(T p0, T1 p1, T2 p2);

        [DisplayName("transfer")]
        public static event MyAction<byte[], byte[], BigInteger> Transferred;
        [DisplayName("refund")]
        public static event MyAction<byte[], BigInteger> Refund;

        /*
        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;

        [DisplayName("refund")]
        public static event Action<byte[], BigInteger> Refund;
        */

        public static Object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                if (Owner.Length == 20)
                {
                    // if param Owner is script hash
                    return Runtime.CheckWitness(Owner);
                }
                else if (Owner.Length == 33)
                {
                    // if param Owner is public key
                    byte[] signature = operation.AsByteArray();
                    return VerifySignature(signature, Owner);
                }
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "deploy") return Deploy();
                if (operation == "mintOptions") return MinOptions();
                if (operation == "totalSupply") return TotalSupply();
                if (operation == "name") return Name();
                if (operation == "symbol") return Symbol();
                if (operation == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }
                if (operation == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }
                if (operation == "decimals") return Decimals();
            }
            //you can choice refund or not refund
            byte[] sender = GetSender();
            ulong contribute_value = GetContributeValue();
            if (contribute_value > 0 && sender.Length != 0)
            {
                Refund(sender, contribute_value);
            }
            return false;
        }

        // initialization parameters, only once
        // 初始化参数
        public static bool Deploy()
        {
            byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
            if (total_supply.Length != 0) return false;
            Storage.Put(Storage.CurrentContext, Owner, pre_option_cap);
            Storage.Put(Storage.CurrentContext, "totalSupply", pre_option_cap);
            Transferred(null, Owner, pre_option_cap);
            return true;
        }

      
        public static bool MinOptions()
        {
            byte[] sender = GetSender();
            // contribute asset is not neo
            if (sender.Length == 0)
            {
                return false;
            }
            ulong contribute_value = GetContributeValue();
            // the current exchange rate between ico options and neo during the option swap period
            ulong swap_rate = CurrentSwapRate();
            if (swap_rate == 0)
            {
                Refund(sender, contribute_value);
                return false;
            }
            // you can get current swap option amount
            ulong option = CurrentSwapOption(sender, contribute_value, swap_rate);
            if (option == 0)
            {
                return false;
            }
            // crowdfunding success
            BigInteger balance = Storage.Get(Storage.CurrentContext, sender).AsBigInteger();
            Storage.Put(Storage.CurrentContext, sender, option + balance);
            BigInteger totalSupply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            Storage.Put(Storage.CurrentContext, "totalSupply", option + totalSupply);
            Transferred(null, sender, option);
            return true;
        }

        // get the total option supply
        // 获取已发行option总量
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }

        // function that is always called when someone wants to transfer options.
        // 流转option调用
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (from == to) return true;
            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);
            Transferred(from, to, value);
            return true;
        }

        // get the account balance of another account with address
        // 根据地址获取option的余额
        public static BigInteger BalanceOf(byte[] address)
        {
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger();
        }

        // The function CurrentSwapRate() returns the current exchange rate
        // between  options and neo during the option swap period
        private static ulong CurrentSwapRate()
        {
            const int ico_duration = option_end_time - option_start_time;
            uint now = Runtime.Time;
            int time = (int)now - option_start_time;
            if (time < 0)
            {
                return 0;
            }
            else if (time < ico_duration)
            {
                return basic_rate;
            }
            else
            {
                return 0;
            }
        }

        //whether over contribute capacity, you can get the option amount
        private static ulong CurrentSwapOption(byte[] sender, ulong value, ulong swap_rate)
        {
            ulong option = value / neo_decimals * swap_rate;
            BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            BigInteger balance_option = total_amount - total_supply;
            if (balance_option <= 0)
            {
                Refund(sender, value);
                return 0;
            }
            else if (balance_option < option)
            {
                Refund(sender, (option - balance_option) / swap_rate * neo_decimals);
                option = (ulong)balance_option;
            }
            return option;
        }

        // check whether asset is neo and get sender script hash
        private static byte[] GetSender()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] reference = tx.GetReferences();
            // you can choice refund or not refund
            foreach (TransactionOutput output in reference)
            {
                if (output.AssetId == neo_asset_id) return output.ScriptHash;
            }
            return new byte[] { };
        }

        // get smart contract script hash
        private static byte[] GetReceiver()
        {
            return ExecutionEngine.ExecutingScriptHash;
        }

        // get all you contribute neo amount
        private static ulong GetContributeValue()
        {
            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Neo
            // 获取转入智能合约地址的Neo总量
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == GetReceiver() && output.AssetId == neo_asset_id)
                {
                    value += (ulong)output.Value;
                }
            }
            return value;
        }
    }
}
