# Not Using Docker

## 2 Node Ready
### AWS
#### Install Library
```
sudo yum install -y gcc-c++ autoconf automake libtool boost-devel openssl-devel libevent-devel
```

#### Download bitcoind
```
curl -L https://github.com/bitcoin/bitcoin/archive/v0.17.0.tar.gz -o bitcoin-0.17.0.tar.gz
tar zxvf bitcoin-0.17.0.tar.gz
cd bitcoin-0.17.0
```

#### Install Berklay DB
```
BITCOIN_ROOT=$(pwd)
BDB_PREFIX="${BITCOIN_ROOT}/db4"
mkdir -p $BDB_PREFIX
wget 'http://download.oracle.com/berkeley-db/db-4.8.30.tar.gz'
tar -xzvf db-4.8.30.tar.gz
cd db-4.8.30/build_unix/
../dist/configure --enable-cxx --disable-shared --with-pic --prefix=$BDB_PREFIX
make install
```

#### Build bitcoind
```
cd $BITCOIN_ROOT
./autogen.sh
./configure LDFLAGS="-L${BDB_PREFIX}/lib/" CPPFLAGS="-I${BDB_PREFIX}/include/"
make && sudo make install && echo DONE
```

#### make bitcoin.conf
```
sudo mkdir /etc/bitcoin
sudo vim /etc/bitcoin/bitcoin.conf
```

#### bitcoin.conf
bitcoind-0.17.0 >=
```
regtest=1

[regtest]
rpcuser=rpcuser
rpcpassword=rpcpassword
rpcport=18443
server=1
rest=1
allowrpcip=<otherrpcip>
allowrpcip=<myrpcip>
```

bitcoind-0.16.3 <=
```
regtest=1
rpcuser=rpcuser
rpcpassword=rpcpassword
rpcport=18443
server=1
rest=1
allowrpcip=<otherrpcip>
allowrpcip=<myrpcip>
```

#### Make Directory
```
sudo mkdir /var/lib/bitcoin
```

#### regist systemd

```
sudo vim /etc/systemd/system/bitcoind.service
```

```
[Unit]
Description=Bitcoin daemon
After=network.target

[Service]
ExecStart=/usr/local/bin/bitcoind -daemon -datadir=/var/lib/bitcoin -conf=/etc/bitcoin/bitcoin.conf -pid=/var/run/bitcoind/bitcoind.pid
RuntimeDirectory=bitcoind
Type=forking
PIDFile=/var/run/bitcoind/bitcoind.pid
Restart=on-failure

# Hardening measures
####################

# Provide a private /tmp and /var/tmp.
PrivateTmp=true

# Mount /usr, /boot/ and /etc read-only for the process.
ProtectSystem=full

# Disallow the process and all of its children to gain
# new privileges through execve().
NoNewPrivileges=true

# Use a new /dev namespace only populated with API pseudo devices
# such as /dev/null, /dev/zero and /dev/random.
PrivateDevices=true

# Deny the creation of writable and executable memory mappings.
MemoryDenyWriteExecute=true

[Install]
WantedBy=multi-user.target
```

If you need reload, exec `sudo systemctl daemon-reload`

#### regist wake up process
```
sudo systemctl enable bitcoind.service
```

#### Wake up process
```
sudo systemctl start bitcoind.service
```

#### Confirmation
```
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf getblockchaininfo
```

#### Synchronyze bitcoind
##### Configure
here is two node that call each `Node A`、`Node B`.
`Node B` add below config.

Node B `bitcoin.conf`
```
addnode=<NodeA IP>:<NodeA port>
```

設定後、bitcoindを再起動します。
```
sudo systemctl restart bitcoind.service
```

##### Confirm synchronyzation

```
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf getblockchaininfo
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf generate 1
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf getblockchaininfo
```

## Install c-lightning

```
sudo yum update
sudo yum install -y autoconf automake build-essential git libtool gmp gmp-devel sqlite sqlite-devel zlib-devel python python3 net-tools
git clone https://github.com/ElementsProject/lightning.git
cd lightning
LIGHTNING=$(pwd)
./configure
make
```

make config
```
mkdir ~/.lightning
vim ~/.lightning/config
```

```
alias=YOUR_FAVORITE_NAME
bitcoin-datadir=/var/lib/bitcoin
bitcoin-rpcconnect=0.0.0.0:18443
bitcoin-rpcuser=rpcuser
bitcoin-rpcpassword=rpcpassword
network=regtest
log-level=debug
log-file=/home/ec2-user/.lightning/debug.log
ignore-fee-limits=true
bind-addr=0.0.0.0
announce-addr=YOURSELF_IP
```

##### c-lightning
```
cd $(LIGHTNING)
lightnind/lightningd --daemon
```

##### Confirm execution
```
cli/lightning-cli getinfo
```

##### Micro Payment
Below from here is the operation of Node A normally.

##### New Address for Lightning
```
cli/lightning-cli newaddr
```

##### Send BTC to the address for Lightning
```
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf sendtoaddress <address> <BTC>

bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf generate 1
```

##### Lightning Connect

`Node B`
```
# copy id
cli/lightning-cli getinfo
```

`Node A`
```
cli/lightning-cli connect <id> <ip address>

# Confirm connection
cli/lightning-cli listpeers
```

##### Open Channel
```
cli/lightning-cli fundchannel <id> <satoshi>

# increase confirmation
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf generate 1

# Check channel status
# increase confirmation until CHANNELD_NORMAL
cli/lightning-cli listpeers
```

##### Payments
`Node B`
```
# copy output bolt11
cli/lightning-cli invoice <msatoshi> <label (uniq)> <description>
# cli/lightning-cli invoice 1000 fromA "A pays to B"

# execute waitinvoice if you want to pay
cli/lightning-cli waitinvoice <label>
```

`Node A`
```
cli/lightning-cli pay <bolt11>
```

##### Close channel
```
# copy peer_id, channel_id or short_channel_id
# use short_channel_id at the example
cli/lightning-cli listchannels
cli/lightning-cli close <short_channel_id>

# Check channel status
# CLOSINGD_SIGEXCHANGE
cli/lightning-cli listpeers

# mining closing transaction
# It make status ONCHAIN when it is mined sometimes.
bitcoin-cli -conf=/etc/bitcoin/bitcoin.conf generate 1
```

#### EC2 Instance Configuration
##### Hosts
Need to edit at all instance.
`sudo vim /etc/hosts`
```
<Alice IP> Alice
<Bob IP> Bob
```

##### Listen api port
```
sudo yum install socat

socat TCP4-listen:9835,fork,reuseaddr UNIX-CONNECT:/home/ec2-user/.lightning/lightning-rpc %
```

#### Change Code

```
--- a/bench/Lightning.Bench/ActorTester.cs
+++ b/bench/Lightning.Bench/ActorTester.cs
@@ -51,11 +51,12 @@ namespace Lightning.Tests
                {
                        RPCUser = Utils.GetVariable("TESTS_RPCUSER", "ceiwHEbqWI83");
                        RPCPassword = Utils.GetVariable("TESTS_RPCPASSWORD", "DwubwWsoo3");
-                       RPCURL = Utils.GetVariable("TESTS_RPCURL", "http://127.0.0.1:24735/");
-                       CLightning = Utils.GetVariable("TESTS_CLIGHTNING", $"tcp://127.0.0.1:{lightningPort}/");
+                       RPCURL = Utils.GetVariable("TESTS_RPCURL", "http://127.0.0.1:43782/");
+                       CLightning = Utils.GetVariable("TESTS_CLIGHTNING", $"tcp://{name}:9835/");
                        Directory = Path.Combine(baseDirectory, name);
                        P2PHost = name;
                        Port = Utils.FreeTcpPort();
                }
 
                public async Task WaitRouteTo(ActorTester destination)


diff --git a/bench/Lightning.Bench/Tester.cs b/bench/Lightning.Bench/Tester.cs
index 3e5de6e..7331f99 100644
--- a/bench/Lightning.Bench/Tester.cs
+++ b/bench/Lightning.Bench/Tester.cs
@@ -40,14 +40,14 @@ namespace Lightning.Tests
                {
                        EnsureCreated(_Directory);
 
-                       if(File.Exists(Path.Combine(cmd.WorkingDirectory, "docker-compose.yml")))
-                               cmd.Run("docker-compose down --v --remove-orphans");
-                       cmd.Run("docker kill $(docker ps -f 'name = lightningbench_ *' -q)");
-                       cmd.Run("docker rm $(docker ps -a -f 'name = lightningbench_ *' -q)");
-                       cmd.Run("docker volume rm $(docker volume ls -f 'name = lightningbench_ *' -q)");
-                       GenerateDockerCompose(actors.Select(a => a.P2PHost).ToArray());
-                       cmd.Run("docker-compose down --v --remove-orphans"); // Makes really sure we start clean
-                       cmd.AssertRun("docker-compose up -d dev");
+                       //if(File.Exists(Path.Combine(cmd.WorkingDirectory, "docker-compose.yml")))
+                       //      cmd.Run("docker-compose down --v --remove-orphans");
+                       //cmd.Run("docker kill $(docker ps -f 'name = lightningbench_ *' -q)");
+                       //cmd.Run("docker rm $(docker ps -a -f 'name = lightningbench_ *' -q)");
+                       //cmd.Run("docker volume rm $(docker volume ls -f 'name = lightningbench_ *' -q)");
+                       //GenerateDockerCompose(actors.Select(a => a.P2PHost).ToArray());
+                       //cmd.Run("docker-compose down --v --remove-orphans"); // Makes really sure we start clean
+                       //cmd.AssertRun("docker-compose up -d dev");
                        foreach(var actor in actors)
                        {
                                actor.Start();


--- a/src/Common/CLightning/NodeInfo.cs
+++ b/src/Common/CLightning/NodeInfo.cs

public NodeInfo(string nodeId, string host, int port)
		{
			if(host == null)
				throw new ArgumentNullException(nameof(host));
			if(nodeId == null)
				throw new ArgumentNullException(nameof(nodeId));
-                       Port = port;
+			Port = (port == 0) ? 9735 : port;
			Host = host;
			NodeId = nodeId;
		}
```

