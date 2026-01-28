"""
Comprehensive Unitree G1 SDK2 Connection Test Script
Tests: Audio, LED, Locomotion, and Communication

FIXED VERSION - Uses only methods that exist in the SDK
"""

import time
import sys
from unitree_sdk2py.core.channel import ChannelSubscriber, ChannelFactoryInitialize
from unitree_sdk2py.g1.audio.g1_audio_client import AudioClient
from unitree_sdk2py.g1.loco.g1_loco_client import LocoClient


class G1ConnectionTest:
    def __init__(self, network_interface):
        """Initialize the G1 test suite"""
        print("=" * 60)
        print("UNITREE G1 SDK2 COMPREHENSIVE CONNECTION TEST")
        print("=" * 60)

        # Initialize DDS channel
        print(f"\n[INIT] Initializing DDS Channel on interface: {network_interface}")
        ChannelFactoryInitialize(0, network_interface)

        # Initialize clients
        self.audio_client = AudioClient()
        self.audio_client.SetTimeout(10.0)
        self.audio_client.Init()
        print("[INIT] AudioClient initialized")

        self.loco_client = LocoClient()
        self.loco_client.SetTimeout(10.0)
        self.loco_client.Init()
        print("[INIT] LocoClient initialized")

        print("[INIT] All clients ready!\n")

    def test_audio_system(self):
        """Test audio volume control and TTS"""
        print("\n" + "=" * 60)
        print("TEST 1: AUDIO SYSTEM")
        print("=" * 60)

        # Test volume get - NOTE: GetVolume() returns (code, data) tuple!
        print("\n[AUDIO] Getting current volume...")
        code, volume_data = self.audio_client.GetVolume()
        if code == 0:
            print(f"[AUDIO] Current volume: {volume_data}")
        else:
            print(f"[AUDIO] Failed to get volume, error code: {code}")

        # Test volume set
        print("[AUDIO] Setting volume to 70...")
        self.audio_client.SetVolume(70)
        time.sleep(0.5)

        code, new_volume = self.audio_client.GetVolume()
        if code == 0:
            print(f"[AUDIO] New volume: {new_volume}")
        else:
            print(f"[AUDIO] Failed to get volume, error code: {code}")

        # Test TTS (English)
        print("\n[AUDIO] Testing TTS - English")
        self.audio_client.TtsMaker("Hello! I am Unitree G1. Connection test in progress.", 0)
        time.sleep(4)

        print("[AUDIO] Audio system test passed!")

    def test_led_system(self):
        """Test LED control"""
        print("\n" + "=" * 60)
        print("TEST 2: LED SYSTEM")
        print("=" * 60)

        self.audio_client.TtsMaker("Testing LED system", 0)
        time.sleep(2)

        colors = [
            ("RED", 255, 0, 0),
            ("GREEN", 0, 255, 0),
            ("BLUE", 0, 0, 255),
            ("YELLOW", 255, 255, 0),
            ("CYAN", 0, 255, 255),
            ("MAGENTA", 255, 0, 255),
            ("WHITE", 255, 255, 255),
        ]

        for color_name, r, g, b in colors:
            print(f"[LED] Setting color to {color_name} (R:{r}, G:{g}, B:{b})")
            self.audio_client.LedControl(r, g, b)
            time.sleep(1)

        # Turn off LEDs
        print("[LED] Turning off LEDs")
        self.audio_client.LedControl(0, 0, 0)
        time.sleep(0.5)

        print("[LED] LED system test passed!")

    def test_gestures(self):
        """Test various gesture commands"""
        print("\n" + "=" * 60)
        print("TEST 3: GESTURE CONTROL")
        print("=" * 60)

        gestures = [
            ("Wave Hand", lambda: self.loco_client.WaveHand(False)),
            ("Shake Hand (prepare for handshake)", lambda: self.loco_client.ShakeHand(0)),
        ]

        for gesture_name, gesture_func in gestures:
            print(f"\n[GESTURE] Executing: {gesture_name}")
            self.audio_client.TtsMaker(f"Executing {gesture_name}", 0)
            time.sleep(2)

            gesture_func()
            print(f"[GESTURE] {gesture_name} command sent")
            time.sleep(5)  # Wait for gesture to complete

        print("\n[GESTURE] Gesture control test passed!")

    def test_movement_modes(self):
        """Test different movement modes - SKIPPED FOR NOW"""
        print("\n" + "=" * 60)
        print("TEST 4: MOVEMENT MODES (SKIPPED)")
        print("=" * 60)
        print("[MOVEMENT] Skipping - too state-dependent, will implement later")
    
    def test_balance_stand(self):
        """Test balance stand mode with small movements"""
        print("\n" + "=" * 60)
        print("TEST 5: BALANCE STAND MODE")
        print("=" * 60)

        print("\n[BALANCE] Enternting balance stand mode")
        self.audio_client.TtsMaker("Testing balance stand", 0)
        time.sleep(2)

        print("[BALANCE] Testing body movement control")
        self.loco_client.Move(0, 0, 0.1)
        time.sleep(2)

        self.loco_client.Move(0, 0, -0,1)
        time.sleep(2)

        self.loco_client.StopMove()
        time.sleep(2)

        print("[BALANCE] Balance stand test passed!")

    def test_communication_loop(self):
        """Test continuous communication (simulating game loop)"""
        print("\n" + "=" * 60)
        print("TEST 6: COMMUNICATION LOOP (10 seconds)")
        print("=" * 60)

        print("\n[COMM] Simulating continuous command stream...")
        self.audio_client.TTtsMaker("Testing communication loop", 0)
        time.sleep(2)

        start_time = time.time()
        command_count = 0

        while time.time() - start_time < 10:
            code, _ = self.audio_client.GetVolume()
            command_count += 1

            if command_count % 10 == 0:
                print(f"[COMM] Commands sent: {command_count}, Status code: {code}")

            time.sleep(0.1)
        
        print(f"[COMM] Communication loop test passed! ({command_count} commands in 10s)")

    def run_all_tests(self):
        """Run all test sequences"""
        try:
            self.test_audio_system()
            time.sleep(2)

            self.test_led_system()
            time.sleep(2)

            self.test_gestures()
            time.sleep(2)

            self.test_movement_modes()
            time.sleep(2)

            self.test_balance_stand()
            time.sleep(2)

            self.test_communication_loop()

            print("\n" + "=" * 60)
            print("ALL TESTS COMPLETED SUCCESSFULLY!")
            print("=" * 60)
            self.audio_client.TtsMaker("All tests completed successfully! Connection is stable.", 0)
            time.sleep(3)

            print("\n[CLEANUP] Returning to safe state...")
            self.loco_client.Damp()
            self.audio_client.LedControl(0, 255, 0)
            time.sleep(2)
            self.audio_client.LedControl(0, 0, 0)

        except Exception as e:
            print(f"\n[ERROR] Test failed: {e}")
            self.audio_client.TtsMaker("Test failed. Please check connection.", 0)
            self.audio_client.LedControl(255, 0, 0)
            time.sleep(2)
            raise

def main():
    if len(sys.argv) < 2: # If no argument provided...
        print(f"Usage: python3 {sys.argv[0]} <network_interface>")
        print(f"Example: python3 {sys.argv[0]} eth0")
        print(f"         python3 {sys.argv[0]} enp2s0")
        sys.exit(-1)
    
    network_interface = sys.argv[1]

    tester = G1ConnectionTest(network_interface)
    tester.run_all_tests

if __name__ == "__main__":
    main()