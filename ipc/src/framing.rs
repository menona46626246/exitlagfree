// SPDX-License-Identifier: Apache-2.0
use crate::IpcError;
use std::io::Read;

/// Limite duro: evita asignaciones gigantes ante un mensaje malformado.
pub const MAX_FRAME: usize = 64 * 1024;

pub fn encode_frame(payload: &[u8]) -> Result<Vec<u8>, IpcError> {
    if payload.is_empty() {
        return Err(IpcError::Frame("payload vacio".into()));
    }
    if payload.len() > MAX_FRAME {
        return Err(IpcError::Frame(format!("{} > {} bytes", payload.len(), MAX_FRAME)));
    }
    let mut out = Vec::with_capacity(4 + payload.len());
    out.extend_from_slice(&(payload.len() as u32).to_be_bytes());
    out.extend_from_slice(payload);
    Ok(out)
}

pub fn parse_frame_len(header: &[u8; 4]) -> Result<usize, IpcError> {
    let len = u32::from_be_bytes(*header) as usize;
    if len == 0 {
        return Err(IpcError::Frame("longitud cero".into()));
    }
    if len > MAX_FRAME {
        return Err(IpcError::Frame(format!("longitud {len} excede {MAX_FRAME}")));
    }
    Ok(len)
}

pub fn read_frame<R: Read>(reader: &mut R) -> Result<Vec<u8>, IpcError> {
    let mut header = [0u8; 4];
    reader.read_exact(&mut header)?;
    let len = parse_frame_len(&header)?;
    let mut payload = vec![0u8; len];
    reader.read_exact(&mut payload)?;
    Ok(payload)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    #[test]
    fn frame_round_trip() {
        let f = encode_frame(b"hola").unwrap();
        assert_eq!(&f[..4], &[0, 0, 0, 4]);
        assert_eq!(read_frame(&mut Cursor::new(f)).unwrap(), b"hola".to_vec());
    }

    #[test]
    fn oversized_frames_are_rejected_before_allocation() {
        let big = vec![0u8; MAX_FRAME + 1];
        assert!(encode_frame(&big).is_err());
        let header = ((MAX_FRAME + 1) as u32).to_be_bytes();
        assert!(parse_frame_len(&header).is_err());
        assert!(parse_frame_len(&[0, 0, 0, 0]).is_err());
        assert_eq!(parse_frame_len(&[0, 0, 0, 1]).unwrap(), 1);
    }

    #[test]
    fn truncated_frame_fails_cleanly() {
        let mut c = Cursor::new(vec![0, 0, 0, 10, 1, 2]);
        assert!(read_frame(&mut c).is_err());
    }
}
