import time
import sys
from unitree_sdk2py.core.channel import ChannelSubscriber, ChannelFactoryInitialize
from unitree_sdk2py.g1.audio.g1_audio_client import AudioClient
from unitree_sdk2py.g1.loco.g1_loco_client import LocoClient

def main():
    if len(sys.argv) < 2:
        print(f"Usage: python3 {sys.argv[0]} networkInterface")
        sys.exit(-1)

    ChannelFactoryInitialize(0, sys.argv[1])

    # Initialize clients 
    audio_client = AudioClient()  
    audio_client.SetTimeout(10.0)
    audio_client.Init()

    loco_client = LocoClient()
    loco_client.SetTimeout(10.0)
    loco_client.Init()

    ret = audio_client.GetVolume()
    print("debug GetVolume: ",ret)

    # Set volume
    audio_client.SetVolume(85)

    ret = audio_client.GetVolume()
    print("debug GetVolume: ",ret)

    # Step 1: Greeting
    print("[1] Greeting...")
    audio_client.TtsMaker("Hello! I am the Unitree G1 robot. Let's shake hands!", 0)
    time.sleep(5)

    # Step 2: Extend hand out
    print("[2] Extending hand...")
    loco_client.ShakeHand(stage=0) # Arm out
    time.sleep(3)

    # Step 3: Retract hand back
    print("[3] Retracing hand...")
    loco_client.ShakeHand(stage=1)
    time.sleep(2)

    # Step 4: Nice to meet you
    print("[4] Closing message...")
    audio_client.TtsMaker("Nice to meet you! Thank you everyone!", 0)
    time.sleep(4)

    print("Demo complete!")

if __name__ == "__main__":
    main()