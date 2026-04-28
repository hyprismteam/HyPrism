def dp(config):
    match config:
        case {"coffe": True}:
            print("☕ Coffee")
            return True
        
        case _:
            return False