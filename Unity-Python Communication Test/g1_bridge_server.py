"""
Unity-Python UDP Communication Bridge for Unitree G1
Runs on Robot Controller (192.168.123.164)
Receives commands from PC Unity (192.168.123.162)
Controls G1 Robot (192.168.123.161) via SDK2
"""

import socket
import json
import threading
import time
import sys
from datetime import datetime

class UnityPythonBridge:
    def __init__(self, pc_ip, listen_port):
        """
        Initialize UDP server for Unity communication
        
        Args:
            pc_ip: IP address of the PC running Unity (192.168.123.162)
            listen_port: Port to listen on (5005)
        """
        self.listen_port = listen_port
        self.pc_ip = pc_ip
        self.pc_port = None  # Will be set when we receive first message
        
        # Create UDP socket
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(('0.0.0.0', self.listen_port))  # Listen on all interfaces
        self.sock.settimeout(0.1)  # Non-blocking with timeout
        
        self.running = False
        self.unity_address = None  # Will be set when we receive first message
        
        print("="*60)
        print("UNITY-PYTHON UDP BRIDGE FOR UNITREE G1")
        print("="*60)
        print(f"[BRIDGE] Server listening on 0.0.0.0:{self.listen_port}")
        print(f"[BRIDGE] Waiting for Unity connection from {self.pc_ip}...")
        print("[BRIDGE] Make sure Unity is running on the PC!")
        
    def send_to_unity(self, message_dict):
        """Send JSON message to Unity"""
        if self.unity_address is None:
            print("[BRIDGE] Warning: Unity address not set, cannot send message")
            return False
            
        try:
            message_json = json.dumps(message_dict)
            self.sock.sendto(message_json.encode('utf-8'), self.unity_address)
            print(f"[BRIDGE → UNITY] {message_dict}")
            return True
        except Exception as e:
            print(f"[BRIDGE] Error sending to Unity: {e}")
            return False
    
    def process_command(self, command_dict):
        """Process command received from Unity"""
        cmd_type = command_dict.get('command', 'unknown')
        
        print(f"\n[BRIDGE] Processing command: {cmd_type}")
        
        # Respond based on command type
        if cmd_type == 'ping':
            response = {
                'status': 'success',
                'command': 'ping',
                'message': 'pong from robot controller',
                'timestamp': datetime.now().isoformat(),
                'controller_ip': '192.168.123.164',
                'robot_ip': '192.168.123.161'
            }
            self.send_to_unity(response)
            
        elif cmd_type == 'test_echo':
            # Echo back whatever data was sent
            response = {
                'status': 'success',
                'command': 'test_echo',
                'echo_data': command_dict.get('data', ''),
                'message': f"Echoed from robot controller: {command_dict.get('data', '')}"
            }
            self.send_to_unity(response)
            
        elif cmd_type == 'shake_hand':
            # Simulate handshake workflow (will be replaced with actual SDK2 calls)
            print("[BRIDGE] Handshake requested - simulating workflow...")
            
            # Send acknowledgment
            self.send_to_unity({
                'status': 'acknowledged',
                'command': 'shake_hand',
                'message': 'Handshake command received, preparing robot'
            })
            
            # Simulate robot execution time
            print("[BRIDGE] Simulating robot extending hand...")
            time.sleep(2)
            
            # Send in-progress update
            self.send_to_unity({
                'status': 'in_progress',
                'command': 'shake_hand',
                'message': 'Robot hand extended, waiting for player'
            })
            
            # Simulate waiting for player handshake
            print("[BRIDGE] Simulating player interaction...")
            time.sleep(3)
            
            # Simulate completion
            print("[BRIDGE] Simulating handshake completion...")
            time.sleep(1)
            
            # Send completion
            self.send_to_unity({
                'status': 'completed',
                'command': 'shake_hand',
                'message': 'Handshake completed, robot returning to idle'
            })
            
        elif cmd_type == 'get_status':
            response = {
                'status': 'success',
                'command': 'get_status',
                'robot_status': 'idle',
                'connection': 'active',
                'controller_ip': '192.168.123.164',
                'robot_ip': '192.168.123.161',
                'message': 'Robot controller is ready'
            }
            self.send_to_unity(response)
            
        else:
            # Unknown command
            response = {
                'status': 'error',
                'command': cmd_type,
                'message': f'Unknown command: {cmd_type}'
            }
            self.send_to_unity(response)
    
    def listen(self):
        """Main listening loop"""
        self.running = True
        
        while self.running:
            try:
                # Receive data from Unity
                data, address = self.sock.recvfrom(4096)
                
                # Verify it's from the expected PC
                if address[0] == self.pc_ip:
                    # Set Unity address on first message
                    if self.unity_address is None:
                        self.unity_address = address
                        print(f"\n[BRIDGE] ✓ Unity connected from {address[0]}:{address[1]}")
                        print(f"[BRIDGE] Communication established!\n")
                    
                    # Decode and parse JSON
                    message = data.decode('utf-8')
                    command_dict = json.loads(message)
                    
                    print(f"[UNITY → BRIDGE] {command_dict}")
                    
                    # Process command in separate thread to avoid blocking
                    threading.Thread(target=self.process_command, args=(command_dict,)).start()
                else:
                    print(f"[BRIDGE] Warning: Received message from unexpected IP: {address[0]}")
                
            except socket.timeout:
                # No data received, continue loop
                continue
            except json.JSONDecodeError as e:
                print(f"[BRIDGE] JSON decode error: {e}")
            except Exception as e:
                if self.running:  # Only print if we're still supposed to be running
                    print(f"[BRIDGE] Error: {e}")
    
    def start(self):
        """Start the bridge server"""
        print("[BRIDGE] Starting server...")
        listener_thread = threading.Thread(target=self.listen)
        listener_thread.daemon = True
        listener_thread.start()
        
        print("\n[BRIDGE] Press Ctrl+C to stop\n")
        
        try:
            while True:
                time.sleep(0.1)
        except KeyboardInterrupt:
            print("\n[BRIDGE] Shutting down...")
            self.stop()
    
    def stop(self):
        """Stop the bridge server"""
        self.running = False
        self.sock.close()
        print("[BRIDGE] Server stopped")

def main():
    if len(sys.argv) < 4:
        print("Usage: python3 unity_python_bridge.py <network_interface> <pc_ip> <listen_port>")
        print("Example: python3 unity_python_bridge.py eth0 192.168.123.162 5005")
        sys.exit(-1)
    
    network_interface = sys.argv[1]  # eth0
    pc_ip = sys.argv[2]              # 192.168.123.162
    listen_port = int(sys.argv[3])   # 5005
    
    print(f"[INIT] Network Interface: {network_interface}")
    print(f"[INIT] PC IP (Unity): {pc_ip}")
    print(f"[INIT] Listen Port: {listen_port}")
    print()
    
    # Create and start bridge
    bridge = UnityPythonBridge(pc_ip, listen_port)
    bridge.start()

if __name__ == "__main__":
    main()